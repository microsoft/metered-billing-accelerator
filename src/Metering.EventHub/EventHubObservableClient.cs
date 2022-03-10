// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Metering.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Runtime.InteropServices;
    using System.Reactive.Disposables;
    using System.Reactive.Linq;
    using Microsoft.Extensions.Logging;
    using Azure.Messaging.EventHubs;
    using Azure.Messaging.EventHubs.Processor;
    using Azure.Messaging.EventHubs.Consumer;
    using Metering.BaseTypes;
    using Metering.BaseTypes.EventHub;

    public static class EventHubObservableClientCSharp
    {
    }
}