using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

/// Central controller for sending network commands and receiving network events.
/// Used by both client and server, so logic should be relatively generic.
public class NetChannel : INetEventListener, INetChannel {
  // TODO: This key should be obfuscated in the build somehow?
  private static string CONNECTION_KEY = "498j98dfa9sd8fh";

  // Connectionless message headers.
  private static byte PING_HEADER = 0x77;
  private static byte PONG_HEADER = 0x78;

  // Listenable events.
  public Action<NetPeer> PeerConnectedHandler { get; set; }
  public Action<NetPeer, DisconnectInfo> PeerDisconnectedHandler { get; set; }

  // Lookup for latency by peer.
  public Dictionary<NetPeer, int> PeerLatency { get; private set; }
      = new Dictionary<NetPeer, int>();

  private bool acceptConnections;
  private NetManager netManager;
  private NetPacketProcessor netPacketProcessor;
  private NetDataWriter netDataWriter;

  // Separate net manager only used for ping/pong.
  private PingHelper pingHelper = new PingHelper();

  // Callbacks for connectionless pings.
  private Dictionary<IPEndPoint, Action<int>> unconnectedPingCallbacks =
      new Dictionary<IPEndPoint, Action<int>>();

  // Debugging stuff.
  private DebugNetworkSettings debugNetworkSettings;
  private float debugLargeStallsTimer;
  private ulong lastBytesSent, lastBytesRecv;
  private Ice.TimedAverage sendAverage = new Ice.TimedAverage(1);
  private Ice.TimedAverage recvAverage = new Ice.TimedAverage(1);

  public NetChannel(DebugNetworkSettings debugNetworkSettings) {
    netManager = new NetManager(this) {
      AutoRecycle = true,
      EnableStatistics = true,
      UnconnectedMessagesEnabled = true, // For ping/pong
    };
    netPacketProcessor = new NetPacketProcessor();
    netDataWriter = new NetDataWriter();

    this.debugNetworkSettings = debugNetworkSettings;
    ApplyDebugNetworkSettings();

    // Register nested types used in net commands.
    NetModels.RegisterAllNetworkNestedTypes(netPacketProcessor);

    // The client network manager is started immediately for unconnected pings.
    netManager.Start();
  }

  /// Update should be called every frame.
  public void Update() {
    netManager.PollEvents();
    UpdateAccumulatedStats();

    // Handle debug timers.
    debugLargeStallsTimer += Time.deltaTime;
    if (debugNetworkSettings.SimulateLargeStalls &&
        debugLargeStallsTimer > debugNetworkSettings.LargeStallsInterval +
            debugNetworkSettings.LargeStallsDuration) {
      ApplyDebugNetworkSettings();
      debugLargeStallsTimer = 0;
    } else if (debugNetworkSettings.SimulateLargeStalls &&
               debugLargeStallsTimer > debugNetworkSettings.LargeStallsInterval) {
      netManager.SimulatePacketLoss = true;
      netManager.SimulateLatency = true;
      netManager.SimulationPacketLossChance = debugNetworkSettings.LargeStallsPacketLoss;
      netManager.SimulationMinLatency = debugNetworkSettings.LargeStallsLatency;
      netManager.SimulationMaxLatency = debugNetworkSettings.LargeStallsLatency;
    }
  }

  /// Stop all networking activity (shuts down the client or server).
  public void Stop() {
    netManager.Stop();
  }

  /// Attempt to connect to an endpoint, the channel will act as a client.
  public void ConnectToServer(string host, int port) {
    //if (netManager.IsRunning) {
    //  this.LogWarning("Network manager already running, doing nothing.");
    //  return;
    //}
    netManager.Connect(host, port, CONNECTION_KEY);
  }

  /// Starts listening for connections, the channel will act as as server.
  public void StartServer(int port) {
    //if (netManager.IsRunning) {
    //  this.LogWarning("Network manager already running, doing nothing.");
    //  return;
    //}
    acceptConnections = true;
    netManager.Stop();
    netManager.Start(port);
  }

  public void PingServer(IPEndPoint endpoint, Action<int> callback) {
    // Send a simple unconnected message to get latency.
    this.Log($"Sending ping message to {endpoint}");
    pingHelper.AddListener(endpoint, callback);
    netManager.SendUnconnectedMessage(new byte[] { PING_HEADER }, endpoint);
  }

  private void ApplyDebugNetworkSettings() {
    netManager.SimulatePacketLoss = debugNetworkSettings.SimulatePacketLoss;
    netManager.SimulateLatency = debugNetworkSettings.SimulateLatency;
    netManager.SimulationPacketLossChance = debugNetworkSettings.PacketLossChance;
    netManager.SimulationMinLatency = debugNetworkSettings.MinLatency;
    netManager.SimulationMaxLatency = debugNetworkSettings.MaxLatency;
  }

  /** INetChannel methods */

  public void Subscribe<T>(Action<T> onReceiveHandler) where T : class, new() {
    netPacketProcessor.SubscribeReusable(onReceiveHandler);
  }

  public void SubscribeNS<T>(Action<T> onReceiveHandler) where T : INetSerializable, new() {
    // TODO: Can't get reusable with this, is that ok?
    netPacketProcessor.SubscribeNetSerializable(onReceiveHandler);
  }

  public void Subscribe<T>(Action<T, NetPeer> onReceiveHandler) where T : class, new() {
    netPacketProcessor.SubscribeReusable(onReceiveHandler);
  }

  public void SubscribeNS<T>(Action<T, NetPeer> onReceiveHandler) where T : INetSerializable, new() {
    netPacketProcessor.SubscribeNetSerializable(onReceiveHandler);
  }

  public void SubscribeQueue<T>(Queue<T> queue) where T : class, new() {
    // TODO: Optimize with a packet pool using queue max size.
    netPacketProcessor.Subscribe((T data) => {
      queue.Enqueue(data);
    }, () => { return new T(); });
  }

  public void SubscribeQueue<T>(Queue<WithPeer<T>> queue) where T : class, new() {
    // TODO: Optimize with a packet pool using queue max size.
    netPacketProcessor.Subscribe((T data, NetPeer peer) => {
      queue.Enqueue(new WithPeer<T> { Peer = peer, Value = data });
    }, () => { return new T(); });
  }

  public void SendCommand<T>(NetPeer peer, T command) where T : class, new() {
    var deliveryMethod = NetCommand.Metadata.DeliveryType[typeof(T)];
    netPacketProcessor.Send(peer, command, deliveryMethod);
  }

  public void SendNSCommand<T>(NetPeer peer, T command) where T : INetSerializable, new() {
    var deliveryMethod = NetCommand.Metadata.DeliveryType[typeof(T)];
    netPacketProcessor.SendNetSerializable(peer, command, deliveryMethod);
  }

  public void BroadcastCommand<T>(T command) where T : class, new() {
    var deliveryMethod = NetCommand.Metadata.DeliveryType[typeof(T)];
    netDataWriter.Reset();
    netPacketProcessor.Write(netDataWriter, command);
    netManager.SendToAll(netDataWriter, deliveryMethod);
  }

  public void BroadcastCommand<T>(T command, NetPeer excludedPeer) where T : class, new() {
    var deliveryMethod = NetCommand.Metadata.DeliveryType[typeof(T)];
    netDataWriter.Reset();
    netPacketProcessor.Write(netDataWriter, command);
    netManager.SendToAll(netDataWriter, deliveryMethod, excludedPeer);
  }

  private void UpdateAccumulatedStats() {
    sendAverage.Update(Time.deltaTime, netManager.Statistics.BytesSent - lastBytesSent);
    recvAverage.Update(Time.deltaTime, netManager.Statistics.BytesReceived - lastBytesRecv);
    this.LogValue("send bytes/s", sendAverage.Average);
    this.LogValue("recv bytes/s", recvAverage.Average);
    lastBytesSent = netManager.Statistics.BytesSent;
    lastBytesRecv = netManager.Statistics.BytesReceived;
  }

  /**
   * Litenet Network events.
   * 
   * These are lower level and handled directly by the channel, exposed as higher level APIs.
   */

  public void OnPeerConnected(NetPeer peer) {
    PeerConnectedHandler?.Invoke(peer);
  }

  public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
    PeerDisconnectedHandler?.Invoke(peer, disconnectInfo);
  }

  public void OnConnectionRequest(ConnectionRequest request) {
    if (!acceptConnections) {
      request.Reject();
    }
    request.AcceptIfKey(CONNECTION_KEY);
  }

  public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod) {
    netPacketProcessor.ReadAllPackets(reader, peer);
  }

  public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
    var header = reader.GetByte();
    if (header == PING_HEADER) {
      this.Log($"Received PING from {remoteEndPoint}");
      netManager.SendUnconnectedMessage(new byte[] { PONG_HEADER }, remoteEndPoint);
    } else if (header == PONG_HEADER) {
      this.Log($"Received PONG from {remoteEndPoint}");
      pingHelper.ReceivePong(remoteEndPoint);
    } else {
      this.LogWarning("Got unexpected unconnected message. Spam/attack?");
    }
  }

  public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) {
    this.LogWarning("Network error - " + socketError);
  }

  public void OnNetworkLatencyUpdate(NetPeer peer, int latency) {
    PeerLatency[peer] = latency;
    this.LogValue("ping", latency);
  }
}
