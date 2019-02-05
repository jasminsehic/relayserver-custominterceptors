using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Serilog;
using Thinktecture.Relay.Server.Interceptor;

namespace Thinktecture.Relay.ForwardedMemcachedCustomCode.Test
{
	[TestClass]
	public class ForwardedRequestInterceptorTest
	{
		private readonly Mock<ILogger> _loggerMock;

		private const string XForwardedForHeader = "X-Forwarded-For";
		private const string XForwardedProtoHeader = "X-Forwarded-Proto";
		private const string XForwardedHostHeader = "X-Forwarded-Host";
		private const string XForwardedPortHeader = "X-Forwarded-Port";
		private const string XForwardedPathHeader = "X-Forwarded-Path";
		private const string ForwardedHeader = "Forwarded";

		public ForwardedRequestInterceptorTest()
		{
			_loggerMock = new Mock<ILogger>();
		}

		[TestMethod]
		public void ForwardedRequestInterceptor_default_configuration_doesnt_leak_private_ip()
		{
			const string url = "https://tt.invalid/";
			var sut = new ForwardedRequestInterceptor(_loggerMock.Object);

			var interceptedRequest = new Mock<IInterceptedRequest>();
			interceptedRequest.SetupGet(c => c.ClientRequestUri).Returns(new Uri(url));
			interceptedRequest.SetupGet(c => c.ClientIpAddress).Returns(IPAddress.Parse("10.1.1.1"));
			var mockHeaders = new Dictionary<string, string>();
			interceptedRequest.SetupGet(c => c.HttpHeaders).Returns(mockHeaders);
			interceptedRequest.Setup(c => c.CloneHttpHeaders()).Returns(mockHeaders);

			sut.OnRequestReceived(interceptedRequest.Object);

			mockHeaders[ForwardedHeader].Should().NotBeNullOrWhiteSpace();
			var forParameterValue = mockHeaders[ForwardedHeader]
				.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
				.Where(key => key.IndexOf("for=", StringComparison.InvariantCultureIgnoreCase) > -1)
				.Select(p => p.Split('='))
				.ToDictionary(kvp => kvp[0].Trim(), kvp => kvp[1].Trim())["for"];

			forParameterValue.Should().StartWith("_", "Default configuration doesn't leak private IP");
		}

		[TestMethod]
		public void ForwardedRequestInterceptor_transitions_xforwarded_headers()
		{
			const string url = "https://tt.invalid/";
			var sut = new ForwardedRequestInterceptor(_loggerMock.Object) { ObfuscateForHeader = false };

			var interceptedRequest = new Mock<IInterceptedRequest>();
			interceptedRequest.SetupGet(c => c.ClientRequestUri).Returns(new Uri(url));
			interceptedRequest.SetupGet(c => c.ClientIpAddress).Returns(IPAddress.Parse("10.1.1.1"));
			var mockHeaders = new Dictionary<string, string>()
			{
				[XForwardedForHeader] = "1.2.3.4",
				[XForwardedProtoHeader] = "http",
				[XForwardedHostHeader] = "platform934.tt.invalid",
				[XForwardedPortHeader] = "80",
				[XForwardedPathHeader] = "/relay/user/target"
			};
			interceptedRequest.SetupGet(c => c.HttpHeaders).Returns(mockHeaders);
			interceptedRequest.Setup(c => c.CloneHttpHeaders()).Returns(mockHeaders);

			sut.OnRequestReceived(interceptedRequest.Object);

			mockHeaders[ForwardedHeader].Should().Be("for=1.2.3.4,for=10.1.1.1;path=/relay/user/target;proto=http;host=platform934.tt.invalid;");
		}

		[TestMethod]
		public void ForwardedRequestInterceptor_transitions_xforwarded_headers_without_host_header()
		{
			const string url = "https://tt.invalid/";
			var sut = new ForwardedRequestInterceptor(_loggerMock.Object) { ObfuscateForHeader = false };

			var interceptedRequest = new Mock<IInterceptedRequest>();
			interceptedRequest.SetupGet(c => c.ClientRequestUri).Returns(new Uri(url));
			interceptedRequest.SetupGet(c => c.ClientIpAddress).Returns(IPAddress.Parse("10.1.1.1"));
			var mockHeaders = new Dictionary<string, string>()
			{
				[XForwardedForHeader] = "1.2.3.4",
				[XForwardedProtoHeader] = "http",
				[XForwardedPortHeader] = "8080",
				[XForwardedPathHeader] = "/relay/user/target"
			};
			interceptedRequest.SetupGet(c => c.HttpHeaders).Returns(mockHeaders);
			interceptedRequest.Setup(c => c.CloneHttpHeaders()).Returns(mockHeaders);

			sut.OnRequestReceived(interceptedRequest.Object);

			mockHeaders[ForwardedHeader].Should().Be("for=1.2.3.4,for=10.1.1.1;path=/relay/user/target;proto=http;host=tt.invalid:8080;");
		}

		[TestMethod]
		public void ForwardedRequestInterceptor_ignores_xforwarded_headers_when_forwarded_header_is_already_present()
		{
			const string url = "https://tt.invalid/path";
			var sut = new ForwardedRequestInterceptor(_loggerMock.Object) { ObfuscateForHeader = false };

			var interceptedRequest = new Mock<IInterceptedRequest>();
			interceptedRequest.SetupGet(c => c.ClientRequestUri).Returns(new Uri(url));
			interceptedRequest.SetupGet(c => c.ClientIpAddress).Returns(IPAddress.Parse("2001:db8:85a3::8a2e:370:7334"));
			var mockHeaders = new Dictionary<string, string>()
			{
				[XForwardedForHeader] = "1.2.3.4",
				[XForwardedHostHeader] = "platform934.tt.invalid",
				[ForwardedHeader] = "for=1.2.3.4;"
			};
			interceptedRequest.SetupGet(c => c.HttpHeaders).Returns(mockHeaders);
			interceptedRequest.Setup(c => c.CloneHttpHeaders()).Returns(mockHeaders);

			sut.OnRequestReceived(interceptedRequest.Object);

			mockHeaders[ForwardedHeader].Should().Be("for=1.2.3.4,for=\"[2001:db8:85a3::8a2e:370:7334]\";host=tt.invalid;proto=https;path=/path;");
		}

		[TestMethod]
		public void ForwardedRequestInterceptor_xforwarded_ipv6_host_headers_is_handled()
		{
			const string url = "https://tt.invalid/path";
			var sut = new ForwardedRequestInterceptor(_loggerMock.Object) { ObfuscateForHeader = false };

			var interceptedRequest = new Mock<IInterceptedRequest>();
			interceptedRequest.SetupGet(c => c.ClientRequestUri).Returns(new Uri(url));
			interceptedRequest.SetupGet(c => c.ClientIpAddress).Returns(IPAddress.Parse("4.4.4.4"));
			var mockHeaders = new Dictionary<string, string>()
			{
				[XForwardedHostHeader] = "[2001:db8:85a3::8a2e:370:7334]"
			};
			interceptedRequest.SetupGet(c => c.HttpHeaders).Returns(mockHeaders);
			interceptedRequest.Setup(c => c.CloneHttpHeaders()).Returns(mockHeaders);

			sut.OnRequestReceived(interceptedRequest.Object);

			mockHeaders[ForwardedHeader].Should().Be("for=4.4.4.4;host=\"[2001:db8:85a3::8a2e:370:7334]\";proto=https;path=/path;");
		}

		[TestMethod]
		public void ForwardedRequestInterceptor_ipv6_intercepted_request_uri_is_handled()
		{
			const string url = "https://[2001:db8:85a3::8a2e:370:7334]/path";
			var sut = new ForwardedRequestInterceptor(_loggerMock.Object) { ObfuscateForHeader = false };

			var interceptedRequest = new Mock<IInterceptedRequest>();
			interceptedRequest.SetupGet(c => c.ClientRequestUri).Returns(new Uri(url));
			interceptedRequest.SetupGet(c => c.ClientIpAddress).Returns(IPAddress.Parse("4.4.4.4"));
			var mockHeaders = new Dictionary<string, string>()
			{
				[XForwardedPortHeader] = "8080"
			};
			interceptedRequest.SetupGet(c => c.HttpHeaders).Returns(mockHeaders);
			interceptedRequest.Setup(c => c.CloneHttpHeaders()).Returns(mockHeaders);

			sut.OnRequestReceived(interceptedRequest.Object);

			mockHeaders[ForwardedHeader].Should().Be("for=4.4.4.4;host=\"[2001:db8:85a3::8a2e:370:7334]:8080\";proto=https;path=/path;");
		}

		[TestMethod]
		public void ForwardedRequestInterceptor_removes_transitioned_xforwarded_headers()
		{
			const string url = "https://tt.invalid/path";
			var sut = new ForwardedRequestInterceptor(_loggerMock.Object) { ObfuscateForHeader = false };

			var interceptedRequest = new Mock<IInterceptedRequest>();
			interceptedRequest.SetupGet(c => c.ClientRequestUri).Returns(new Uri(url));
			interceptedRequest.SetupGet(c => c.ClientIpAddress).Returns(IPAddress.Parse("2001:db8:85a3::8a2e:370:7334"));
			var mockHeaders = new Dictionary<string, string>()
			{
				[XForwardedForHeader] = "1.2.3.4",
				[XForwardedHostHeader] = "8.8.8.8",
				[XForwardedPortHeader] = "8000",
			};
			interceptedRequest.SetupGet(c => c.HttpHeaders).Returns(mockHeaders);
			interceptedRequest.Setup(c => c.CloneHttpHeaders()).Returns(mockHeaders);

			sut.OnRequestReceived(interceptedRequest.Object);

			mockHeaders.Count.Should().Be(1);
			mockHeaders.ContainsKey(ForwardedHeader).Should().Be(true);
			mockHeaders[ForwardedHeader].Should().Be("for=1.2.3.4,for=\"[2001:db8:85a3::8a2e:370:7334]\";host=8.8.8.8:8000;proto=https;path=/path;");
		}

		[TestMethod]
		public void ForwardedRequestInterceptor_applies_forwarded_header()
		{
			const string url = "http://[2001:db8:85a3::8a2e:370:7334]:20000/relay";
			var sut = new ForwardedRequestInterceptor(_loggerMock.Object) { ObfuscateForHeader = false };

			var interceptedRequest = new Mock<IInterceptedRequest>();
			interceptedRequest.SetupGet(c => c.ClientRequestUri).Returns(new Uri(url));
			interceptedRequest.SetupGet(c => c.ClientIpAddress).Returns(IPAddress.Parse("10.1.1.1"));
			var mockHeaders = new Dictionary<string, string>();
			interceptedRequest.SetupGet(c => c.HttpHeaders).Returns(mockHeaders);
			interceptedRequest.Setup(c => c.CloneHttpHeaders()).Returns(mockHeaders);

			sut.OnRequestReceived(interceptedRequest.Object);

			mockHeaders[ForwardedHeader].Should().Be("for=10.1.1.1;host=\"[2001:db8:85a3::8a2e:370:7334]:20000\";proto=http;path=/relay;");
		}
	}
}
