// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes.EventHub

open System
open Metering.BaseTypes

type PingReason =
    | ProcessingStarting
    | TopOfHour

/// Represents a regular message sent by the aggregator as a heartbeat signal.
type PingMessage =
    { PartitionID: PartitionID
      PingReason: PingReason
      LocalTime: MeteringDateTime
      SendingHost: string }

module PingMessage =
    let create partitionId reason =
        { PartitionID = partitionId
          PingReason = reason
          SendingHost = Environment.MachineName
          LocalTime = MeteringDateTime.now() }