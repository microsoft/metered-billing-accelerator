// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

module Metering.NUnitTests.RuntimeUnitTest

open Metering.BaseTypes
open Metering.Integration
open Metering.BaseTypes.EventHub
open NUnit.Framework

[<Test>]
let ``checkGenerateSnapshot``() =
    let get (dur: string option, evt: string option) =
        function
        | "MAX_DURATION_BETWEEN_SNAPSHOTS" -> dur
        | "MAX_NUMBER_OF_EVENTS_BETWEEN_SNAPSHOTS" -> evt
        | _ -> None

    let cfg dur evt = MeteringConnections.loadSnapshotIntervalConfigurationFromEnvironment (get (dur, evt))

    let time sequenceNr dateTimeStr = MessagePosition.create "0" sequenceNr (MeteringDateTime.fromStr dateTimeStr)

    Assert.IsTrue(
        cfg (Some "00:05:00") (Some "2000")
        |> SnapshotIntervalConfiguration.shouldCreateSnapshot
            (time    0l "2024-01-01T00:00:00Z")
            (time 2001l "2024-01-01T00:00:01Z")
        )

    Assert.IsFalse(
        cfg (Some "00:05:00") (Some "2000")
        |> SnapshotIntervalConfiguration.shouldCreateSnapshot
            (time    0l "2024-01-01T00:00:00Z")
            (time 1999l "2024-01-01T00:00:01Z")
        )

    Assert.IsFalse(
        cfg (Some "1.00:00:00") None
        |> SnapshotIntervalConfiguration.shouldCreateSnapshot
            (time   0l "2024-01-01T00:00:00Z")
            (time 999l "2024-01-01T00:00:01Z"))

    Assert.IsFalse(
        cfg None (Some "2000")
        |> SnapshotIntervalConfiguration.shouldCreateSnapshot
            (time    0l "2024-01-01T00:00:00Z")
            (time 1999l "2024-01-01T00:00:01Z"))

    Assert.IsTrue(
        cfg None (Some "2000")
        |> SnapshotIntervalConfiguration.shouldCreateSnapshot
            (time    0l "2024-01-01T00:00:00Z")
            (time 1999l "2024-01-01T01:00:01Z"))

    // Without configuration, we should use the default values (at least hourly, or every 1000 events)
    Assert.IsTrue(
        cfg None None
        |> SnapshotIntervalConfiguration.shouldCreateSnapshot
            (time    0l "2024-01-01T00:00:00Z")
            (time 1001l "2024-01-01T00:00:01Z"))
