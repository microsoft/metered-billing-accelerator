// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

module Metering.NUnitTests.BusinessLogicTests

open System
open System.IO
open System.Text.RegularExpressions
open NUnit.Framework
open Metering.BaseTypes
open Metering.BaseTypes.EventHub

type E = EventHubEvent<MeteringUpdateEvent>
type S = MeterCollection

#nowarn "0342"

[<CustomComparison; StructuralEquality>]
type TestFile =
    | Event of E
    | State of (SequenceNumber * S)
    interface IComparable<TestFile> with
        member this.CompareTo other =
            match (this, other) with
            | (Event(e1), Event(e2)) ->  e1.MessagePosition.SequenceNumber.CompareTo(e2.MessagePosition.SequenceNumber)
            | (State(sn1, _), State(sn2, _)) -> sn1.CompareTo(sn2)
            | (Event(e1), State(sn2, _)) -> if e1.MessagePosition.SequenceNumber = sn2 then -1 else e1.MessagePosition.SequenceNumber.CompareTo(sn2)
            | (State(sn1, _), Event(e2)) -> if sn1 = e2.MessagePosition.SequenceNumber then 1 else sn1.CompareTo(e2.MessagePosition.SequenceNumber)
    interface IComparable with
        member this.CompareTo obj =
            match obj with
                | null                 -> 1
                | :? TestFile as other -> (this :> IComparable<_>).CompareTo other
                | _                    -> invalidArg "obj" $"not a {nameof(TestFile)}"

let private readFile (name: string) : TestFile option =
    let filename = (new FileInfo(name)).Name

    // 000--event--2021-11-04--16-12-26--SubscriptionPurchased-2-year.json
    let regexPatternEvent = "^(?<sequenceNumber>\d+)--event--(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})--(?<hour>\d{2})-(?<minute>\d{2})-(?<second>\d{2})--(?<comment>.*)\.json$"
    let regexEvent = new Regex(pattern = regexPatternEvent, options = RegexOptions.ExplicitCapture)
    let matchEvent = regexEvent.Match(input = filename)

    // 000-state.json
    let regexPatternState = "^(?<sequenceNumber>\d+)--state\.json$"
    let regexState = new Regex(pattern = regexPatternState, options = RegexOptions.ExplicitCapture)
    let matchState = regexState.Match(input = filename)

    match (matchEvent.Success, matchState.Success) with
    | (true, false) ->
        let sequenceNumber = matchEvent.Groups["sequenceNumber"].Value |> SequenceNumber.Parse
        let g (name: string) = matchEvent.Groups[name].Value |> System.Int32.Parse
        let date = MeteringDateTime.create ("year" |> g) ("month" |> g) ("day" |> g) ("hour" |> g) ("minute" |> g) ("second" |> g)
        let partitionId = "0"
        let messagePosition = MessagePosition.create partitionId sequenceNumber date
        let _comment = matchEvent.Groups["comment"].Value
        let meteringUpdateEvent = File.ReadAllText(name) |> Json.fromStr<MeteringUpdateEvent>

        E.createEventHub meteringUpdateEvent messagePosition None
        |> Event
        |> Some
    | (false, true) ->
        let sequenceNumber = matchState.Groups["sequenceNumber"].Value |> SequenceNumber.Parse
        let state = File.ReadAllText(name) |> Json.fromStr<S>
        (sequenceNumber, state) |> State |> Some
    | _ -> None

let readTestFolder (path: string) : TestFile seq =
    Directory.GetFiles(path, searchPattern = "*.json")
    |> Seq.choose readFile
    |> Seq.sort

[<Test>]
let ``Comparator works`` () =
    let e = File.ReadAllText("data/BusinessLogic/RefreshIncludedQuantities/000--event--2021-11-04--16-12-26--SubscriptionPurchased-2-year.json") |> Json.fromStr<MeteringUpdateEvent>
    let s = File.ReadAllText("data/BusinessLogic/RefreshIncludedQuantities/000--state.json") |> Json.fromStr<S>
    let newEvent sequenceNumber =
        let sequenceNumber = sequenceNumber|> SequenceNumber.Parse
        let messagePosition = MessagePosition.create "0" sequenceNumber (MeteringDateTime.fromStr "2021-11-04T16:12:26Z")
        E.createEventHub e messagePosition None |> Event
    let newState sequenceNumber = TestFile.State (sequenceNumber|> SequenceNumber.Parse, s)

    let l = [
        newEvent "1"
        newState "1"
        newEvent "2"
        newState "2"
    ]

    Assert.IsTrue((l = List.sort l)) // Ensure the list is already sorted

let getEvents (files: TestFile seq) = files |> Seq.choose (function | Event e -> Some e | _ -> None)

let getStates (files: TestFile seq) = files |> Seq.choose (function | State s -> Some s  | _ -> None)

let getState (files: TestFile seq) (idx: SequenceNumber) : MeterCollection =
    files
    |> getStates
    |> Seq.find (fun (a, _) -> a = idx)
    |> fun (_, i) -> i

#nowarn "0067"

let private writeCalculatedState folder (sn: SequenceNumber) (s: S) =
    let path = (new FileInfo(Path.Combine(folder, $"%03d{sn}--state-calculated.json"))).FullName
    File.WriteAllText(path, contents = (Json.toStr 2 s))
    eprintfn "Wrote actual state to %s" path
    ()

let private checkFolder folder =
    let files = readTestFolder folder
    let events = getEvents files
    let states = getStates files

    let applyMeterToStateAndAssertExpectedState (initialState: S) ((event,expectedState): E*S) : S =
        let actualState = MeterCollectionLogic.handleMeteringEvent initialState event

        try
            Assert.AreEqual(expectedState, actualState)
        with :? Exception ->
            // When the comparison fails, we write the actual state into a file for better inspection
            writeCalculatedState folder (event.MessagePosition.SequenceNumber) actualState
            reraise()

        actualState

    // This is a sequence of tuples containing an Event and the expected state resulting from applying the event to the input state.
    let eventsAndStates : (E * S) seq =
        Seq.zip events states
        |> Seq.map (fun (event, (_, state)) -> (event, state))

    eventsAndStates
    |> Seq.fold applyMeterToStateAndAssertExpectedState MeterCollection.Empty
    |> ignore


[<Test>]
let ``check event sequence 'data/BusinessLogic/RefreshIncludedQuantities'`` () =
    checkFolder "data/BusinessLogic/RefreshIncludedQuantities"

[<Test>]
let ``check event sequence 'data/BusinessLogic/RefreshIncludedQuantitiesMonthly'`` () =
    checkFolder "data/BusinessLogic/RefreshIncludedQuantitiesMonthly"

let private checkOneState folder (initialStateNumber: SequenceNumber) =
    let files = readTestFolder folder
    let eventMessage = files |> getEvents |> Seq.find (fun e -> e.MessagePosition.SequenceNumber = (initialStateNumber + 1L))
    let initialState = getState files initialStateNumber
    let expectedTargetState = getState files (initialStateNumber + 1L)

    let calculatedTargetState = MeterCollectionLogic.handleMeteringEvent initialState eventMessage

    try
        Assert.AreEqual(expectedTargetState, calculatedTargetState)
    with :? Exception ->
        // When the comparison fails, we write the actual state into a file for better inspection
        writeCalculatedState folder (initialStateNumber + 1L) calculatedTargetState
        reraise()

[<Test>]
let ``check 'data/BusinessLogic/RefreshIncludedQuantitiesMonthly' number 1`` () =
    checkOneState "data/BusinessLogic/RefreshIncludedQuantitiesMonthly" 1L

