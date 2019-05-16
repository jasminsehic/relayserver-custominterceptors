## Thinktecture.RelayServer.ForwardedMemcachedCustomCode

Custom interceptor for `Thinktecture.RelayServer` that provides [Memcached](https://memcached.org/) as a temporary shared store for its large request and response delivery (i.e. anything over 64kb) to overcome the SignalR limitations.

Custom interceptor also makes `Thinktecture.RelayServer` [RFC 7239](https://tools.ietf.org/html/rfc7239) compliant and Forwarded HTTP Header aware. 
The Forwarded HTTP Header handler automatically upgrades any legacy forwarded headers including X-Forwarded-For, X-Forwarded-Proto, X-Forwarded-Port, X-Forwarded-Host and X-Forwarded-Path.

## Configuration
Thinktecture.Relay.ForwardedMemcachedCustomCode.dll.config can be used to configure the custom code. Available settings are:
1. `ObfuscateForHeader` (true/false) - Allows you to control whether you want to obfuscate the client information in the Forwarded header to prevent leaking any internal network information. True by default.
1. `TemporaryRequestStorageMemcachedNodeEndPoint` (string) - Connection string to a single Memcached Node end-point. Format is `{IP/DNS}:{PORT}`
1. `TemporaryRequestStorageMemcachedConfigEndPoint` (string) - Connection string to a Memcached Configuration end-point. This is a bespoke Amazon AWS Auto Discovery service that keeps track of all available nodes in a cluster. Format is `{IP/DNS}:{PORT}`
1. `TemporaryRequestStoragePeriod` (timespan) - Allows you to define the Memcached expiry time. Default is 00:00:10 (i.e. 10 seconds).

## Notes
- Custom Code is compatible with `Thinktecture.RelayServer` v2.1.0 and higher.
- Forwarded HTTP Header handles a non-standard parameter `Path` which is commonly used however not a standard. This is required to ensure target application can re-write its links and adjust the path.
