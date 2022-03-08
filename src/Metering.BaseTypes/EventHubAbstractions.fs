// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Types

open System.Runtime.CompilerServices

type SequenceNumber = int64

type PartitionID = PartitionID of string

[<Extension>]
module PartitionID =
    [<Extension>]
    let value (PartitionID x) = x
    let create x = (PartitionID x)

type PartitionKey = PartitionKey of string

[<Extension>]
module PartitionKey =
    [<Extension>]
    let value (PartitionKey x) = x
    let create x = (PartitionKey x)

type PartitionIdentifier =
    | PartitionID of PartitionID
    | PartitionKey of PartitionKey

module PartitionIdentifier =
    let createId = PartitionID.create >> PartitionID
    let createKey = PartitionKey.create >> PartitionKey

type MessagePosition = 
    { PartitionID: PartitionID
      SequenceNumber: SequenceNumber
      // Offset: int64
      PartitionTimestamp: MeteringDateTime }

type StartingPosition =
    | Earliest
    | NextEventAfter of LastProcessedSequenceNumber: SequenceNumber * PartitionTimestamp: MeteringDateTime

module MessagePosition =
    let startingPosition (someMessagePosition: MessagePosition option) : StartingPosition =
        match someMessagePosition with
        | None -> Earliest
        | Some pos -> NextEventAfter(
            LastProcessedSequenceNumber = pos.SequenceNumber, 
            PartitionTimestamp = pos.PartitionTimestamp)
    
    let createData (partitionId: string) (sequenceNumber: int64) (partitionTimestamp: MeteringDateTime) =
        { PartitionID = partitionId |> PartitionID.create
          SequenceNumber = sequenceNumber
          PartitionTimestamp = partitionTimestamp }