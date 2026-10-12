using System.Reflection;
using System.Text;
using Il2CppFishNet;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Managing;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;

namespace ChemistryStationMkII;

// Native FishNet carries the packet; no Steam IDs, save data or telemetry are sent.
[RegisterTypeInIl2Cpp]
[Il2CppImplements(typeof(Il2CppFishNet.Broadcast.IBroadcast))]
public sealed class AfterHoursSessionPacket : Il2CppSystem.Object
{
    public AfterHoursSessionPacket(IntPtr pointer) : base(pointer) { }
    public AfterHoursSessionPacket() : base(ClassInjector.DerivedConstructorPointer<AfterHoursSessionPacket>())
        => ClassInjector.DerivedConstructorBody(this);
}

public sealed partial class Mod
{
    internal static int SettingsRevision;
    private static bool NetworkIsClient => attachedManager != null && attachedManager.ClientManager.Started &&
        !attachedManager.ServerManager.Started;
    private static NetworkManager? attachedManager;
    private static bool wasHost, wasClient, clientApproved;
    private static string hostSession = "", clientSession = "";
    private static int clientRevision;
    private static float nextNetworkPoll, clientDeadline;
    private static Action? deferredClientData;
    private sealed class Peer
    {
        internal NetworkConnection Connection = null!;
        internal int Ack, Sent;
        internal float Deadline, LastSent;
        internal Action? DeferredData;
    }
    private static readonly Dictionary<int, Peer> Peers = new();
    // IL2CPP may rewrap packet objects; pointer-keyed payloads avoid relying on managed instance fields.
    private static readonly Dictionary<IntPtr, string> WirePayloads = new();
    private static readonly List<object> NetworkDelegates = new();
    private static Il2CppSystem.Action<AfterHoursSessionPacket>? clientReceiver;
    private static Il2CppSystem.Action<NetworkConnection, AfterHoursSessionPacket>? serverReceiver;
    private static bool networkWireVerified;

    private void InstallNetworkPatches()
    {
        Patch(typeof(LoadManager), "StartGame", prefix: nameof(NetworkLoadStarting));
        Patch(typeof(LoadManager), "LoadAsClient", prefix: nameof(NetworkLoadStarting));
        Patch(typeof(LoadManager), "ExitToMenu", prefix: nameof(NetworkEnding));
        Patch(typeof(Player), "RequestPlayerData_Server", prefix: nameof(GateClientPlayerData));
        Patch(typeof(Player), "SetPlayerData_Client", prefix: nameof(GateHostPlayerData));
    }

    private static T Keep<T>(T? callback) where T : class { if (callback == null) throw new InvalidOperationException("Native delegate creation failed."); NetworkDelegates.Add(callback); return callback; }

    private static void NetworkLoadStarting()
    {
        NetworkEnding();
        try { AttachNetwork(); }
        catch (Exception ex) { Report("network initialization", ex); }
    }

    private static void AttachNetwork()
    {
        var manager = InstanceFinder.NetworkManager;
        if (manager == null || attachedManager != null && manager.Pointer == attachedManager.Pointer) return;
        if (attachedManager != null)
        {
            attachedManager.ClientManager.UnregisterBroadcast<AfterHoursSessionPacket>(clientReceiver!);
            attachedManager.ServerManager.UnregisterBroadcast<AfterHoursSessionPacket>(serverReceiver!);
        }
        if (NetworkDelegates.Count == 0)
        {
            GenericWriter<AfterHoursSessionPacket>.Write = Keep(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Writer, AfterHoursSessionPacket>>(
                new Action<Writer, AfterHoursSessionPacket>(WritePacket)));
            GenericReader<AfterHoursSessionPacket>.Read = Keep(DelegateSupport.ConvertDelegate<Il2CppSystem.Func<Reader, AfterHoursSessionPacket>>(
                new Func<Reader, AfterHoursSessionPacket>(ReadPacket)));
            clientReceiver = Keep(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<AfterHoursSessionPacket>>(new Action<AfterHoursSessionPacket>(ReceiveHostSettings)));
            serverReceiver = Keep(DelegateSupport.ConvertDelegate<Il2CppSystem.Action<NetworkConnection, AfterHoursSessionPacket>>(new Action<NetworkConnection, AfterHoursSessionPacket>(ReceiveClientAck)));
        }
        manager.ClientManager.RegisterBroadcast<AfterHoursSessionPacket>(clientReceiver!);
        manager.ServerManager.RegisterBroadcast<AfterHoursSessionPacket>(serverReceiver!, true);
        attachedManager = manager;
        VerifyNetworkWire(manager);
        MelonLogger.Msg("[After Hours] Network settings channel registered; matching DLL and game builds required.");
    }

    private static void VerifyNetworkWire(NetworkManager manager)
    {
        if (networkWireVerified) return;
        var packet = new AfterHoursSessionPacket();
        string expected = JsonConvert.SerializeObject(new SessionMessage { Kind = "settings", Build = BuildFingerprint,
            Game = Application.version, Session = Guid.NewGuid().ToString("N"), Revision = 1, Settings = RuntimeTuning.Current });
        WirePayloads[packet.Pointer] = expected;
        var writer = WriterPool.Retrieve(manager);
        PooledReader? reader = null;
        try
        {
            GenericWriter<AfterHoursSessionPacket>.Write.Invoke(writer, packet);
            reader = ReaderPool.Retrieve(writer.GetArraySegment(), manager, (Reader.DataSource)0);
            var decoded = GenericReader<AfterHoursSessionPacket>.Read.Invoke(reader);
            if (TakePacket(decoded) != expected || reader.Remaining != 0)
                throw new InvalidOperationException("Native settings wire round trip failed.");
            NetworkProtocol.Parse(expected, BuildFingerprint, Application.version);
            networkWireVerified = true;
            MelonLogger.Msg("[After Hours] PASS native FishNet settings serializer round trip (in memory, no network traffic).");
        }
        finally
        {
            WirePayloads.Remove(packet.Pointer);
            if (reader != null) ReaderPool.Store(reader);
            WriterPool.Store(writer);
        }
    }

    private static void WritePacket(Writer writer, AfterHoursSessionPacket packet)
    {
        if (!WirePayloads.TryGetValue(packet.Pointer, out string? payload)) throw new InvalidOperationException("Missing outgoing settings packet.");
        var bytes = Encoding.UTF8.GetBytes(payload);
        if (bytes.Length > NetworkProtocol.MaximumBytes) throw new InvalidOperationException("Settings packet too large.");
        writer.WriteUInt16((ushort)bytes.Length);
        foreach (byte value in bytes) writer.WriteByte(value);
    }

    private static AfterHoursSessionPacket ReadPacket(Reader reader)
    {
        int count = reader.ReadUInt16();
        var packet = new AfterHoursSessionPacket();
        if (count > NetworkProtocol.MaximumBytes || count > reader.Remaining)
        {
            // Consume only this broadcast's available data; the receiver rejects the empty packet.
            for (int i = 0, remaining = Math.Min(count, reader.Remaining); i < remaining; i++) reader.ReadByte();
            WirePayloads[packet.Pointer] = "";
            return packet;
        }
        var bytes = new byte[count];
        for (int i = 0; i < count; i++) bytes[i] = reader.ReadByte();
        if (WirePayloads.Count > 64) WirePayloads.Clear();
        WirePayloads[packet.Pointer] = Encoding.UTF8.GetString(bytes);
        return packet;
    }

    private static string TakePacket(AfterHoursSessionPacket packet)
    {
        if (!WirePayloads.Remove(packet.Pointer, out string? payload)) throw new FormatException("Missing incoming settings packet.");
        return payload;
    }

    private static void SendPacket(SessionMessage message, NetworkConnection? destination = null)
    {
        message.Build = BuildFingerprint;
        message.Game = Application.version;
        var packet = new AfterHoursSessionPacket();
        WirePayloads[packet.Pointer] = JsonConvert.SerializeObject(message);
        try
        {
            if (destination != null) destination.Broadcast(packet, true, Channel.Reliable);
            else InstanceFinder.ClientManager.Broadcast(packet, Channel.Reliable);
        }
        finally { WirePayloads.Remove(packet.Pointer); }
    }

    private static Peer GetPeer(NetworkConnection connection)
    {
        if (!Peers.TryGetValue(connection.ClientId, out var peer) || peer.Connection.Pointer != connection.Pointer)
            Peers[connection.ClientId] = peer = new Peer { Connection = connection, Deadline = Time.realtimeSinceStartup + 180 };
        return peer;
    }

    private static void SendSettings(Peer peer)
    {
        if (peer.Sent != SettingsRevision)
        { peer.Sent = SettingsRevision; peer.Deadline = Time.realtimeSinceStartup + 180; }
        peer.LastSent = Time.realtimeSinceStartup;
        SendPacket(new SessionMessage { Kind = "settings", Session = hostSession, Revision = SettingsRevision, Settings = RuntimeTuning.Current }, peer.Connection);
        DebugEvent("network.send", $"Settings revision {SettingsRevision} sent to peer {peer.Connection.ClientId}");
    }

    private static void ReceiveHostSettings(AfterHoursSessionPacket packet)
    {
        try
        {
            string payload = TakePacket(packet);
            if (!NetworkIsClient) return;
            if (!networkWireVerified) throw new InvalidOperationException("Native network self-check has not passed.");
            var message = NetworkProtocol.Parse(payload, BuildFingerprint, Application.version);
            if (message.Kind != "settings") throw new FormatException("Expected host settings.");
            if (clientSession != "" && clientSession != message.Session) throw new FormatException("Host session changed unexpectedly.");
            if (message.Revision < clientRevision) return;
            if (!EnsureDefinition() || !EnsureOvenDefinition() || !EnsureBoardDefinition())
            { DebugEvent("network.wait", "Waiting for local registry/self-checks before acknowledging host"); return; }
            wasClient = true;
            ApplyTuning(message.Settings!, "host session (local preference file unchanged)");
            clientSession = message.Session;
            clientRevision = message.Revision;
            clientApproved = true;
            SendPacket(new SessionMessage { Kind = "ack", Session = clientSession, Revision = clientRevision });
            sessionStatus.Value = $"Client — host revision {clientRevision}";
            var deferred = deferredClientData; deferredClientData = null; deferred?.Invoke();
            DebugEvent("network.accept", $"Host settings accepted, revision {clientRevision}");
        }
        catch (Exception ex)
        {
            MelonLogger.Error("[After Hours] Co-op handshake rejected: " + ex.Message);
            sessionStatus.Value = "Co-op rejected — see Latest.log";
            InstanceFinder.ClientManager.StopConnection();
        }
    }

    private static void ReceiveClientAck(NetworkConnection connection, AfterHoursSessionPacket packet)
    {
        try
        {
            string payload = TakePacket(packet);
            if (!InstanceFinder.ServerManager.Started || !connection.Authenticated || connection.IsLocalClient) return;
            if (!networkWireVerified) throw new InvalidOperationException("Native network self-check failed.");
            var message = NetworkProtocol.Parse(payload, BuildFingerprint, Application.version);
            if (message.Kind != "ack" || message.Session != hostSession || message.Revision > SettingsRevision)
                throw new FormatException("Invalid client acknowledgement.");
            var peer = GetPeer(connection);
            peer.Ack = Math.Max(peer.Ack, message.Revision);
            if (peer.Ack == SettingsRevision)
            {
                var deferred = peer.DeferredData; peer.DeferredData = null; deferred?.Invoke();
                MelonLogger.Msg($"[After Hours] Peer {connection.ClientId} confirmed matching build and settings revision {peer.Ack}.");
            }
        }
        catch (Exception ex)
        {
            MelonLogger.Error("[After Hours] Client handshake rejected: " + ex.Message);
            connection.Disconnect(true);
        }
    }

    private static void UpdateNetwork()
    {
        if (Time.realtimeSinceStartup < nextNetworkPoll) return;
        nextNetworkPoll = Time.realtimeSinceStartup + .5f;
        try
        {
            AttachNetwork();
            bool host = InstanceFinder.ServerManager != null && InstanceFinder.ServerManager.Started;
            bool client = NetworkIsClient;
            if (!host && !client)
            {
                if (wasHost || wasClient) NetworkEnding();
                return;
            }
            if (host && hostSession == "") { hostSession = Guid.NewGuid().ToString("N"); ApplyTuning(localTuning, "host preferences"); }
            if (client && !wasClient) { if (deferredClientData == null) clientDeadline = float.PositiveInfinity; clientApproved = false; }
            wasHost = host; wasClient = client;
            if (!networkWireVerified)
            {
                if (client) InstanceFinder.ClientManager.StopConnection();
                else foreach (var pair in InstanceFinder.ServerManager!.Clients)
                    if (!pair.Value.IsLocalClient) pair.Value.Disconnect(true);
                sessionStatus.Value = "Co-op disabled — native wire check failed; see Latest.log";
                return;
            }
            if (host)
            {
                var active = new HashSet<int>();
                foreach (var pair in InstanceFinder.ServerManager!.Clients)
                {
                    var connection = pair.Value;
                    if (connection.IsLocalClient || !connection.Authenticated) continue;
                    active.Add(connection.ClientId);
                    var peer = GetPeer(connection);
                    if (peer.Ack == SettingsRevision) continue;
                    if (Time.realtimeSinceStartup > peer.Deadline && peer.Sent == SettingsRevision)
                    {
                        MelonLogger.Warning($"[After Hours] Disconnecting peer {connection.ClientId}: no matching build/settings acknowledgement. Every player must install the same DLL.");
                        connection.Disconnect(true); Peers.Remove(connection.ClientId); continue;
                    }
                    if (Time.realtimeSinceStartup - peer.LastSent >= 2) SendSettings(peer);
                }
                foreach (int id in Peers.Keys.Where(id => !active.Contains(id)).ToArray()) Peers.Remove(id);
                sessionStatus.Value = $"Host — {Peers.Values.Count(p => p.Ack == SettingsRevision)}/{Peers.Count} peers synced, revision {SettingsRevision}";
            }
            else if (!clientApproved)
            {
                sessionStatus.Value = "Client — waiting for matching host";
                if (Time.realtimeSinceStartup > clientDeadline)
                {
                    MelonLogger.Error("[After Hours] No compatible host settings received. Leaving session; the host also needs this exact DLL.");
                    InstanceFinder.ClientManager.StopConnection();
                }
            }
        }
        catch (Exception ex) { Report("network update", ex); }
    }

    private static bool GateClientPlayerData(Player __instance, string __0, bool __1)
    {
        if (!NetworkIsClient || clientApproved) return true;
        if (deferredClientData == null) clientDeadline = Time.realtimeSinceStartup + 180;
        deferredClientData = () => { if (__instance != null) __instance.RequestPlayerData_Server(__0, __1); };
        DebugEvent("network.gate", "Waiting for host settings before requesting player inventory");
        return false;
    }

    private static bool GateHostPlayerData(Player __instance, NetworkConnection __0, FullPlayerData __1)
    {
        if (InstanceFinder.ServerManager == null || !InstanceFinder.ServerManager.Started || __0 == null || __0.IsLocalClient) return true;
        var peer = GetPeer(__0);
        if (networkWireVerified && peer.Ack == SettingsRevision) return true;
        peer.DeferredData = () => { if (__instance != null) __instance.SetPlayerData_Client(__0, __1); };
        return false;
    }

    private static void NetworkEnding()
    {
        Peers.Clear(); WirePayloads.Clear(); deferredClientData = null;
        hostSession = clientSession = ""; clientRevision = 0;
        wasHost = wasClient = clientApproved = false;
        nextNetworkPoll = 0;
        ApplyTuning(localTuning, "local preferences restored");
        if (sessionStatus != null) sessionStatus.Value = "Local preferences — no remote host";
    }
}
