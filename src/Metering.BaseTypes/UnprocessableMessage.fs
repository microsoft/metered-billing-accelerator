// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open Metering.BaseTypes.EventHub

type UnprocessableMessage =
    | UnprocessableStringContent of string
    | UnprocessableByteContent of byte array

type RemoveUnprocessedMessagesSelection =
    | BeforeIncluding of SequenceNumber
    | Exactly of SequenceNumber

type RemoveUnprocessedMessages =
    { PartitionID: PartitionID
      Selection: RemoveUnprocessedMessagesSelection }

