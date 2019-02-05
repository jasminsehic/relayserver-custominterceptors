using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Serilog;
using Thinktecture.Relay.Server.Interceptor;

namespace Thinktecture.Relay.ForwardedMemcachedCustomCode
{
	// Add support for Forwarded headers as per HTTP Extension RFC 7239
	public class ForwardedRequestInterceptor : IOnPremiseRequestInterceptor
	{
		private const string XForwardedForHeader = "X-Forwarded-For";
		private const string XForwardedProtoHeader = "X-Forwarded-Proto";
		private const string XForwardedHostHeader = "X-Forwarded-Host";
		private const string XForwardedPortHeader = "X-Forwarded-Port";
		private const string XForwardedPathHeader = "X-Forwarded-Path";
		private readonly string[] _xForwardedHeadersSought;

		private const string ForwardedHeader = "Forwarded";
		private const string ForParameterPrefix = "for=";
		private const string ProtocolParameter = "proto";
		private const string HostParameter = "host";
		private const string PathParameter = "path";

		private const string HttpProtocol = "http";
		private const string HttpsProtocol = "https";
		private const int HttpPort = 80;
		private const int HttpsPort = 443;

		private readonly ILogger _logger;
		private Boolean _obfuscateForHeader;

		public Boolean ObfuscateForHeader
		{
			get => _obfuscateForHeader;
			set => _obfuscateForHeader = value;
		}

		public ForwardedRequestInterceptor(ILogger logger)
		{
			ReadConfiguration();

			_logger = logger;
			_xForwardedHeadersSought = new[]
			{
				XForwardedForHeader, XForwardedProtoHeader, XForwardedHostHeader, XForwardedPortHeader, XForwardedPathHeader
			};
		}

		private void ReadConfiguration()
		{
			_obfuscateForHeader = true;
			var config = ConfigurationManager.OpenExeConfiguration(typeof(ForwardedRequestInterceptor).Assembly.Location);
			if (config.HasFile)
			{
				Boolean.TryParse(config.AppSettings.Settings["ObfuscateForHeader"].Value, out _obfuscateForHeader);
			}
		}

		public HttpResponseMessage OnRequestReceived(IInterceptedRequest request)
		{
			var headers = request.CloneHttpHeaders();
			TransitionXForwardedHeaders(headers, request);
			RemoveXForwardedHeaders(headers);
			ApplyForwardedHeader(headers, request);
			request.HttpHeaders = headers;
			return null;
		}

		private void TransitionXForwardedHeaders(Dictionary<string, string> headers, IInterceptedRequest request)
		{
			// abort if there is nothing to transition and assume if that if both exist that transition has already been done
			if (!headers.Any(ContainsXForwardedHeaders) || headers.ContainsKey(ForwardedHeader))
				return;

			var forwadedHeaderValue = string.Empty;

			if (headers.ContainsKey(XForwardedForHeader))
			{
				var xForwardedFor = headers[XForwardedForHeader].Trim();
				var xForList = xForwardedFor
					.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
					.Select(p => p.Trim()).ToList();
				if (xForList.Any())
				{
					xForwardedFor = String.Join(",", xForList.Select(f => $"{ForParameterPrefix}{f}"));
					forwadedHeaderValue = $"{forwadedHeaderValue}{xForwardedFor};";
				}
			}

			if (headers.ContainsKey(XForwardedPathHeader))
			{
				var xForwardedPath = headers[XForwardedPathHeader].Trim();
				if (!String.IsNullOrWhiteSpace(xForwardedPath))
					forwadedHeaderValue = $"{forwadedHeaderValue}{PathParameter}={xForwardedPath};";
			}

			var xForwardedProto = String.Empty;
			if (headers.ContainsKey(XForwardedProtoHeader))
			{
				xForwardedProto = headers[XForwardedProtoHeader].Trim();
				if (xForwardedProto == HttpProtocol || xForwardedProto == HttpsProtocol)
					forwadedHeaderValue = $"{forwadedHeaderValue}{ProtocolParameter}={xForwardedProto};";
			}

			var xForwardedPort = String.Empty;
			if (headers.ContainsKey(XForwardedPortHeader))
			{
				if (ushort.TryParse(headers[XForwardedPortHeader].Trim(), out var port))
				{
					xForwardedPort = xForwardedProto == HttpProtocol && port == HttpPort || xForwardedProto == HttpsProtocol && port == HttpsPort
						? String.Empty
						: $":{port}";
				}
			}

			var xForwardedHost = String.Empty;
			if (headers.ContainsKey(XForwardedHostHeader))
			{
				xForwardedHost = headers[XForwardedHostHeader].Trim();
				if (!String.IsNullOrWhiteSpace(xForwardedHost))
				{
					if (IPAddress.TryParse(xForwardedHost, out var hostIpAddress))
					{
						if (hostIpAddress.AddressFamily == AddressFamily.InterNetworkV6)
							xForwardedHost = $"\"{xForwardedHost}{xForwardedPort}\"";
						else
							xForwardedHost = $"{xForwardedHost}{xForwardedPort}";
					}
				}
			}

			if (String.IsNullOrWhiteSpace(xForwardedHost) && !String.IsNullOrWhiteSpace(xForwardedPort))
			{
				xForwardedHost = request.ClientRequestUri.Host;
				if (IPAddress.TryParse(xForwardedHost, out var hostIpAddress))
				{
					xForwardedHost = $"{xForwardedHost}{xForwardedPort}";
					if (hostIpAddress.AddressFamily == AddressFamily.InterNetworkV6)
						xForwardedHost = $"\"{xForwardedHost}\"";		
				}
				else
					xForwardedHost = $"{xForwardedHost}{xForwardedPort}";
			}
			
			if (!String.IsNullOrWhiteSpace(xForwardedHost))
				forwadedHeaderValue = $"{forwadedHeaderValue}{HostParameter}={xForwardedHost}";

			headers.Add(ForwardedHeader, forwadedHeaderValue);
		}

		private bool ContainsXForwardedHeaders(KeyValuePair<string, string> kvp)
		{
			return _xForwardedHeadersSought.Contains(kvp.Key, StringComparer.InvariantCultureIgnoreCase);
		}

		private void RemoveXForwardedHeaders(Dictionary<string, string> headers)
		{
			foreach (var kvp in headers.ToArray())
				if (_xForwardedHeadersSought.Contains(kvp.Key, StringComparer.InvariantCultureIgnoreCase))
					headers.Remove(kvp.Key);
		}

		private void ApplyForwardedHeader(Dictionary<string, string> headers, IInterceptedRequest request)
		{
			var forwardedProto = request.ClientRequestUri.Scheme;
			var forwardedPath = String.Join(String.Empty, request.ClientRequestUri.Segments.Take(4)).TrimEnd('/'); 
			var forwardedPort = forwardedProto == HttpProtocol && request.ClientRequestUri.Port == HttpPort || forwardedProto == HttpsProtocol && request.ClientRequestUri.Port == HttpsPort
					? String.Empty
					: $":{request.ClientRequestUri.Port}";
			var forwardedHost = $"{request.ClientRequestUri.Host}{forwardedPort}";
			if (IPAddress.TryParse(forwardedHost, out var hostIpAddress) && hostIpAddress.AddressFamily == AddressFamily.InterNetworkV6)
				forwardedHost = $"\"{forwardedHost}\"";

			String forwardedFor;
			if (_obfuscateForHeader)
			{
				forwardedFor = $"_{Guid.NewGuid().ToString().Substring(0, 8)}";
			}
			else
			{
				forwardedFor = request.ClientIpAddress.AddressFamily == AddressFamily.InterNetworkV6
					? $"\"[{request.ClientIpAddress}]\""
					: $"{request.ClientIpAddress}";
			}

			if (headers.ContainsKey(ForwardedHeader))
			{
				var forParameter = headers[ForwardedHeader]
					.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries)
					.Where(key => key.IndexOf(ForParameterPrefix, StringComparison.InvariantCultureIgnoreCase) > -1)
					.Select(p => p).SingleOrDefault();
				
				if (forParameter != null)
					forParameter = $"{forParameter},";
				var forwardedHeaderValue = $"{forParameter}{ForParameterPrefix}{forwardedFor};";

				var otherParameters = headers[ForwardedHeader]
					.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries)
					.Where(key => key.IndexOf(ForParameterPrefix, StringComparison.InvariantCultureIgnoreCase) == -1)
					.Select(p => p.Split('='))
					.ToDictionary(kvp => kvp[0].Trim(), kvp => kvp[1].Trim());

				if (!otherParameters.ContainsKey(HostParameter))
					otherParameters[HostParameter] = forwardedHost;

				if (!otherParameters.ContainsKey(ProtocolParameter))
					otherParameters[ProtocolParameter] = forwardedProto;

				if (!otherParameters.ContainsKey(PathParameter) && !String.IsNullOrWhiteSpace(forwardedPath))
					otherParameters[PathParameter] = forwardedPath; // not RFC but commonly used

				headers[ForwardedHeader] = $"{forwardedHeaderValue}{String.Join(";", otherParameters.Select(kvp => String.Format($"{kvp.Key}={kvp.Value}")))};";
			}
			else
			{
				var forwardedHeaderValue = $"{ForParameterPrefix}{forwardedFor};{HostParameter}={forwardedHost};{ProtocolParameter}={forwardedProto};";
				if (!String.IsNullOrWhiteSpace(forwardedPath))
					forwardedHeaderValue = $"{forwardedHeaderValue}{PathParameter}={forwardedPath};";

				headers.Add(ForwardedHeader, forwardedHeaderValue);
			}
		}
	}
}
