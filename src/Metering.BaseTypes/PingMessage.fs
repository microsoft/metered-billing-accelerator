// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open System
open Metering.BaseTypes.EventHub

type PingReason =
    | ProcessingStarting
    | TopOfHour

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