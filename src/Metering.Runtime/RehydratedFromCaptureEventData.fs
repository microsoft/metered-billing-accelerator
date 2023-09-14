// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.EventHub

open System
open System.Collections.Generic
open Azure.Messaging.EventHubs
open Metering.BaseTypes.EventHub
open Metering.BaseTypes

type RehydratedFromCaptureEventData(
    blobName: string, eventBody: byte[],
    properties: IDictionary<string, obj>, systemProperties: IReadOnlyDictionary<string, obj>,
    sequenceNumber: int64, offset: int64, enqueuedTime: DateTimeOffset, partitionKey: string) =
    inherit EventData(new BinaryData(eventBody), properties, systemProperties, sequenceNumber, offset, enqueuedTime, partitionKey)
    member this.BlobName = blobName

module RehydratedFromCaptureEventData =
    let getMessagePosition (partitionId: PartitionID) (e: EventData) : MessagePosition =
        { PartitionID = partitionId
          SequenceNumber = e.SequenceNumber
          PartitionTimestamp = MeteringDateTime.fromDateTimeOffset e.EnqueuedTime}
