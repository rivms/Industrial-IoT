﻿// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Microsoft.Azure.IIoT.Modules.OpcUa.Publisher.Controllers {
    using Microsoft.Azure.IIoT.Hub;
    using Microsoft.Azure.IIoT.Module.Framework;
    using Microsoft.Azure.IIoT.OpcUa.Edge.Publisher;
    using Microsoft.Azure.IIoT.Serializers;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Synchronizes dataset writers with cloud endpoint
    /// </summary>
    [Version(1)]
    [Version(2)]
    public class DataSetWriterSettingsController : ISettingsController {

        /// <summary>
        /// Set endpoint url
        /// </summary>
        public string ServiceEndpoint {
            get => _writers.ServiceEndpoint;
            set => _writers.ServiceEndpoint = value;
        }

        /// <summary>
        /// Called to add, update or remove writers
        /// </summary>
        /// <param name="dataSetWriterId"></param>
        /// <returns></returns>
        public VariantValue this[string dataSetWriterId] {
            //
            // See publisher registry - every writer is prefixed to allow query of all
            // writers in the twin using the prefix and underscore - strip of the prefix
            // and underscore to get the actual writer id.  Value is utc time stamp as
            // string which allows us to log the change here also.
            //
            set {
                var writerId = dataSetWriterId.Replace(IdentityType.DataSet + "_", "");
                try {
                    if (value.IsNull()) {
                        _writers.OnDataSetWriterRemoved(writerId);
                        _logger.Information("DataSet {writerId} removed from group.",
                            writerId);
                    }
                    else {
                        _writers.OnDataSetWriterChanged(writerId);
                        _logger.Information("DataSet {writerId} changed ({timestamp})",
                            writerId, (string)value);
                    }
                }
                catch (Exception ex) {
                    _logger.Error(ex,
                        "Error submitting writer change {WriterId}", writerId);
                }
            }
            get {
                var writerId = dataSetWriterId.Replace(IdentityType.DataSet + "_", "");
                if (!_writers.LoadState.TryGetValue(writerId, out var result)) {
                    result = null;
                }
                return result;
            }
        }

        /// <summary>
        /// Create controller with service
        /// </summary>
        /// <param name="writers"></param>
        /// <param name="logger"></param>
        public DataSetWriterSettingsController(IDataSetWriterRegistryLoader writers,
            ILogger logger) {
            _writers = writers ?? throw new ArgumentNullException(nameof(writers));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public IEnumerable<string> GetPropertyNames() {
            return _writers.LoadState.Keys.Select(
                dataSetWriterId => IdentityType.DataSet + "_" + dataSetWriterId);
        }

        private readonly IDataSetWriterRegistryLoader _writers;
        private readonly ILogger _logger;
    }
}
