﻿// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Microsoft.Azure.IIoT.OpcUa.Edge.Publisher.Services {
    using Microsoft.Azure.IIoT.OpcUa.Edge.Publisher.Models;
    using Microsoft.Azure.IIoT.OpcUa.Core;
    using Microsoft.Azure.IIoT.OpcUa.Protocol;
    using Opc.Ua;
    using Opc.Ua.Extensions;
    using Opc.Ua.PubSub;
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;

    /// <summary>
    /// Publisher monitored item message encoder
    /// </summary>
    public class BinarySampleMessageEncoder : INetworkMessageEncoder {

        /// <inheritdoc/>
        public string MessageScheme => MessageSchemaTypes.MonitoredItemMessageBinary;

        /// <inheritdoc/>
        public uint NotificationsDroppedCount { get; private set; }

        /// <inheritdoc/>
        public uint NotificationsProcessedCount { get; private set; }

        /// <inheritdoc/>
        public uint MessagesProcessedCount { get; private set; }

        /// <inheritdoc/>
        public double AvgNotificationsPerMessage { get; private set; }

        /// <inheritdoc/>
        public double AvgMessageSize { get; private set; }

        /// <inheritdoc/>
        public IEnumerable<NetworkMessageModel> EncodeBatch(
            IEnumerable<DataSetMessageModel> messages, int maxMessageSize) {

            var notifications = GetMonitoredItemMessages(messages);
            if (notifications.Count() == 0) {
                yield break;
            }
            var encodingContext = messages.First().ServiceMessageContext;
            var current = notifications.GetEnumerator();
            var processing = current.MoveNext();
            var messageSize = 4; // array length size
            maxMessageSize -= 2048; // reserve 2k for header
            var chunk = new Collection<MonitoredItemMessage>();
            while (processing) {
                var notification = current.Current;
                var messageCompleted = false;
                if (notification != null) {
                    var helperEncoder = new BinaryEncoder(encodingContext);
                    helperEncoder.WriteEncodeable(null, notification);
                    var notificationSize = helperEncoder.CloseAndReturnBuffer().Length;
                    if (notificationSize > maxMessageSize) {
                        // we cannot handle this notification. Drop it.
                        // TODO Trace
                        NotificationsDroppedCount++;
                        processing = current.MoveNext();
                    }
                    else {
                        messageCompleted = maxMessageSize < (messageSize + notificationSize);

                        if (!messageCompleted) {
                            chunk.Add(notification);
                            NotificationsProcessedCount++;
                            processing = current.MoveNext();
                            messageSize += notificationSize;
                        }
                    }
                }
                if (!processing || messageCompleted) {
                    var encoder = new BinaryEncoder(encodingContext);
                    encoder.WriteBoolean(null, true); // is Batch
                    encoder.WriteEncodeableArray(null, chunk);
                    var encoded = new NetworkMessageModel {
                        Body = encoder.CloseAndReturnBuffer(),
                        Timestamp = DateTime.UtcNow,
                        ContentType = ContentMimeType.UaBinary,
                        MessageSchema = MessageSchemaTypes.MonitoredItemMessageBinary
                    };
                    AvgMessageSize = (AvgMessageSize * MessagesProcessedCount + encoded.Body.Length) /
                        (MessagesProcessedCount + 1);
                    AvgNotificationsPerMessage = (AvgNotificationsPerMessage * MessagesProcessedCount +
                        chunk.Count) / (MessagesProcessedCount + 1);
                    MessagesProcessedCount++;
                    chunk.Clear();
                    messageSize = 4;
                    yield return encoded;
                }
            }
        }

        /// <inheritdoc/>
        public IEnumerable<NetworkMessageModel> Encode(
            IEnumerable<DataSetMessageModel> messages, int maxMessageSize) {

            var notifications = GetMonitoredItemMessages(messages);
            if (notifications.Count() == 0) {
                yield break;
            }
            var encodingContext = messages.First().ServiceMessageContext;
            foreach (var networkMessage in notifications) {
                var encoder = new BinaryEncoder(encodingContext);
                encoder.WriteBoolean(null, false); // is not Batch
                encoder.WriteEncodeable(null, networkMessage);
                networkMessage.Encode(encoder);
                var encoded = new NetworkMessageModel {
                    Body = encoder.CloseAndReturnBuffer(),
                    Timestamp = DateTime.UtcNow,
                    ContentType = ContentMimeType.UaBinary,
                    MessageSchema = MessageSchemaTypes.MonitoredItemMessageBinary
                };
                if (encoded.Body.Length > maxMessageSize) {
                    // this message is too large to be processed. Drop it
                    // TODO Trace
                    NotificationsDroppedCount++;
                    yield break;
                }
                NotificationsProcessedCount++;
                AvgMessageSize = (AvgMessageSize * MessagesProcessedCount + encoded.Body.Length) /
                    (MessagesProcessedCount + 1);
                AvgNotificationsPerMessage = (AvgNotificationsPerMessage * MessagesProcessedCount + 1) /
                    (MessagesProcessedCount + 1);
                MessagesProcessedCount++;
                yield return encoded;
            }
        }

        /// <summary>
        /// Produce Monitored Item Messages from the data set message model
        /// </summary>
        /// <param name="messages"></param>
        private IEnumerable<MonitoredItemMessage> GetMonitoredItemMessages(
            IEnumerable<DataSetMessageModel> messages) {
            foreach (var message in messages) {
                foreach (var notification in message.Notifications) {
                    var result = new MonitoredItemMessage {
                        MessageContentMask = (message.Writer?.MessageSettings?
                            .DataSetMessageContentMask).ToMonitoredItemMessageMask(
                                message.Writer?.DataSetFieldContentMask),
                        ApplicationUri = message.ApplicationUri,
                        EndpointUrl = message.EndpointUrl,
                        ExtensionFields = message.Writer?.DataSet?.ExtensionFields,
                        NodeId = notification.NodeId.ToExpandedNodeId(message.ServiceMessageContext.NamespaceUris),
                        Timestamp = message.TimeStamp ?? DateTime.UtcNow,
                        Value = notification.Value,
                        DisplayName = notification.DisplayName,
                        SequenceNumber = notification.SequenceNumber.GetValueOrDefault(0)
                    };
                    // force published timestamp into to source timestamp for the legacy heartbeat compatibility
                    if (notification.IsHeartbeat &&
                        ((result.MessageContentMask & (uint)MonitoredItemMessageContentMask.Timestamp) == 0) &&
                        ((result.MessageContentMask & (uint)MonitoredItemMessageContentMask.SourceTimestamp) != 0)) {
                        result.Value.SourceTimestamp = result.Timestamp;
                    }
                    yield return result;
                }
            }
        }
    }
}