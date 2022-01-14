namespace Metering.Types

open Metering.Types.EventHub

type UnprocessableMessage =
    | UnprocessableStringContent of string
    | UnprocessableByteContent of byte array

type RemoveUnprocessedMessagesSelection =
    | BeforeIncluding of SequenceNumber
    | Exactly of SequenceNumber

type RemoveUnprocessedMessages =
    { PartitionID: PartitionID
      Selection: RemoveUnprocessedMessagesSelection }

