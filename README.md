# Akka.Remote.LocalOnlyIPC
Proposal for Local Only Akka.Remote Transport(s) - using NamedPipes or UnixDomainSockets

This is only a proposal of a Akka.Remote.Transport that can be used for LOCAL ONLY Inter-Process-Communication.
On Windows machines, the NamedPipe Connection can be used. Starting with .Net Standard 2.1 UnixDomainSockets can be used,
they are supposed to be supported on all platforms.

# Things to be discussed / considered

This transport is inspired by the TestTransport class of Akka.Remote and currently has no Unit-Tests. I don't know if it's implemented correctly.
Especially the implementation of the AssociationHandle (Disassociate) is only tested in a live system.

Other thoughts:
- There is a fixed number of simultaneous connections to one ActorSystem (100 for NamedPipes).
- To play nicely with HostName / Port combinations, this Transport uses ConnectionName / ConnectionNumber pairs. A well known ConnectionNumber (1-32767) should be specified for "Server"-ActorSystems, whereas "Client"-ActorSystems can specify 0 as ConnectionNumber (in which case, a random ConnectionNumber in the range 32768-65535 will be used)
- Both for the NamedPipe and the UnixDomainSocket a uniqe name has to found. Whether the used logic is robust enough has to be discussed.
- When creating the AssociationHandle, a dummy localAddress / remoteAddress are passed into the base AssociationHandle-class. I do not know if these have to be the "correct" addresses.
- A basic Hosting-Extension is provided, that can define the four configuration values that are used in this transport. As Scheme-Identifier "pipe" and "uds" may be used right now.
- Preparing a new inbound connection can possibly fail (when, for example, the name for the used pipe already exists). It is not clear what to do in such an event.
- It is not completely clear, what to do when a Transport is shut down. Right now I assume, that all used resourced get cleaned up correctly, but I cannot prove it (no unit tests...)
- I used both the NamedPipe and UnixDomainSockets connections only roughly as a proof of concept on a windows machine. It seems to work so far.


Can be used for discussion at https://github.com/akkadotnet/akka.net/discussions/7187
