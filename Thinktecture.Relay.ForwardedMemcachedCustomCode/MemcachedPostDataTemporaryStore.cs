using System;
using System.Configuration;
using System.IO;
using Amazon.ElastiCacheCluster;
using Enyim.Caching;
using Enyim.Caching.Configuration;
using Enyim.Caching.Memcached;
using Serilog;
using Thinktecture.Relay.Server;

namespace Thinktecture.Relay.ForwardedMemcachedCustomCode
{
	internal class MemcachedPostDataTemporaryStore : IPostDataTemporaryStore, IDisposable
	{
		private static readonly Byte[] _emptyByteArray = new byte[0];

		private readonly ILogger _logger;
		private readonly TimeSpan _storagePeriod;
		private readonly MemcachedClient _memcachedClient;

		public MemcachedPostDataTemporaryStore(ILogger logger)
		{
			_logger = logger;
			var configuration = ReadConfiguration();

			TimeSpan.TryParse(configuration["TemporaryRequestStoragePeriod"].Value, out _storagePeriod);
			var temporaryRequestStorageMemcachedNodeEndPoint = configuration["TemporaryRequestStorageMemcachedNodeEndPoint"].Value;
			var temporaryRequestStorageMemcachedConfigEndPoint = configuration["TemporaryRequestStorageMemcachedConfigEndPoint"].Value;

			if (String.IsNullOrWhiteSpace(temporaryRequestStorageMemcachedNodeEndPoint) && String.IsNullOrWhiteSpace(temporaryRequestStorageMemcachedConfigEndPoint))
				throw new ConfigurationErrorsException($"{nameof(MemcachedPostDataTemporaryStore)}: Either Memcached Node or ElastiCache Memcached Config endpoint must be provided.");
			if (_storagePeriod <= TimeSpan.Zero)
				throw new ConfigurationErrorsException($"{nameof(MemcachedPostDataTemporaryStore)}: Storage period must be positive. Provided value: {_storagePeriod}");
			
			IMemcachedClientConfiguration memcachedConfig;
			if (!String.IsNullOrWhiteSpace(temporaryRequestStorageMemcachedConfigEndPoint))
			{
				var memcachedConfigEndPoint = ParseEndPoint(temporaryRequestStorageMemcachedConfigEndPoint, "config");
				memcachedConfig = new ElastiCacheClusterConfig(memcachedConfigEndPoint.Item1, memcachedConfigEndPoint.Item2);
			}
			else
			{
				memcachedConfig = new MemcachedClientConfiguration();
				var memcachedNodeEndPoint = ParseEndPoint(temporaryRequestStorageMemcachedNodeEndPoint, "node");
				((MemcachedClientConfiguration)memcachedConfig).AddServer(memcachedNodeEndPoint.Item1, memcachedNodeEndPoint.Item2);
			}
			_memcachedClient = new MemcachedClient(memcachedConfig);
		}

		private static KeyValueConfigurationCollection ReadConfiguration()
		{
			var config = ConfigurationManager.OpenExeConfiguration(typeof(MemcachedPostDataTemporaryStore).Assembly.Location);
			return config.HasFile ? config.AppSettings.Settings : new KeyValueConfigurationCollection();
		}

		private Tuple<String, Int32> ParseEndPoint(String endpoint, String kind)
		{
			var port = 11211;
			var parts = endpoint.Split(new[] { ':' }, StringSplitOptions.None);
			if (parts.Length == 0 || parts.Length > 2)
				throw new ArgumentException($"{nameof(MemcachedPostDataTemporaryStore)}: Invalid endpoint for Memcached {kind}");
			if (parts.Length == 2)
				Int32.TryParse(parts[1], out port);
			return new Tuple<String, Int32>(parts[0], port);
		}

		public Stream CreateRequestStream(string requestId)
		{
			_logger?.Verbose("Creating stream for storing request body. request-id={RequestId}", requestId);

			var ms = new NotifyingMemoryStream();
			ms.Disposing += (s, e) => _memcachedClient.Store(StoreMode.Set, requestId, (s as NotifyingMemoryStream)?.ToArray(), _storagePeriod);
			return ms;
		}

		public Stream GetRequestStream(string requestId)
		{
			_logger?.Verbose("Creating stream for stored request body. request-id={RequestId}", requestId);

			return new MemoryStream(_memcachedClient.Get(requestId) as Byte[] ?? _emptyByteArray);
		}

		public Stream CreateResponseStream(string requestId)
		{
			_logger?.Verbose("Creating stream for storing response body. request-id={RequestId}", requestId);

			var ms = new NotifyingMemoryStream();
			ms.Disposing += (s, e) => _memcachedClient.Store(StoreMode.Set, requestId, (s as NotifyingMemoryStream)?.ToArray(), _storagePeriod);
			return ms;
		}

		public Stream GetResponseStream(string requestId)
		{
			_logger?.Verbose("Creating stream for stored response body. request-id={RequestId}", requestId);

			return new MemoryStream(_memcachedClient.Get(requestId) as Byte[] ?? _emptyByteArray);
		}

		public long RenameResponseStream(String temporaryId, String requestId)
		{
			_logger?.Verbose("Renaming stored response body. temporary-id={TemporaryId}, request-id={RequestId}", temporaryId, requestId);

			var temporaryRequest = _memcachedClient.Get(requestId) as Byte[] ?? _emptyByteArray;
			_memcachedClient.Store(StoreMode.Set, requestId, temporaryRequest, _storagePeriod);
			return temporaryRequest.LongLength;
		}

		public void Dispose()
		{
			Dispose(true);
		}

		private void Dispose(bool disposing)
		{
			if (disposing)
			{
				_memcachedClient.Dispose();
			}
		}
	}

	internal class NotifyingMemoryStream : MemoryStream
	{
		public event EventHandler Disposing;

		protected override void Dispose(bool disposing)
		{
			var handler = Disposing;
			handler?.Invoke(this, EventArgs.Empty);

			base.Dispose(disposing);
		}
	}
}
