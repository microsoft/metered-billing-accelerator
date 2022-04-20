// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.BaseTypes

open Metering.BaseTypes.EventHub

/// Represents an unprocessable message in the state file. 
/// 
/// For debugging purposes, messages which cannot properly be parsed are included in the state file. 
type UnprocessableMessage =
    | UnprocessableStringContent of string
    | UnprocessableByteContent of byte array

type RemoveUnprocessedMessagesSelection =
    /// Remove all unprocessable messages prior to, and including, the given sequence number.
    | BeforeIncluding of SequenceNumber
    /// Remove unprocessable message with given sequence number.
    | Exactly of SequenceNumber

/// Command to clean unprocessable messages from the state file.
type RemoveUnprocessedMessages =
    { PartitionID: PartitionID
      Selection: RemoveUnprocessedMessagesSelection }

