using LiteNetLib.Utils;
using UnityEngine;


/// Shared network data structures.
public static class NetModels {
  public static void RegisterAllNetworkNestedTypes(NetPacketProcessor processor) {
    processor.RegisterNestedType(
        NetExtensions.SerializeVector3, NetExtensions.DeserializeVector3);
    processor.RegisterNestedType(
        NetExtensions.SerializeQuaternion, NetExtensions.DeserializeQuaternion);
    processor.RegisterNestedType<PlayerSetupData>();
    processor.RegisterNestedType<PlayerMetadata>();
    processor.RegisterNestedType<InitialPlayerState>();
    processor.RegisterNestedType<PlayerState>();
    processor.RegisterNestedType<NetworkObjectState>();
  }
}

/// Data entered by a player when joining a game.
public struct PlayerSetupData : INetSerializable {
  public string Name;

  public void Serialize(NetDataWriter writer) {
    writer.Put(Name);
  }

  public void Deserialize(NetDataReader reader) {
    Name = reader.GetString();
  }
}

/// Metadata for a player.
public struct PlayerMetadata : INetSerializable {
  public string Name;

  public void Serialize(NetDataWriter writer) {
    writer.Put(Name);
  }

  public void Deserialize(NetDataReader reader) {
    Name = reader.GetString();
  }
}

/// Initial player state sent once to each client.
public struct InitialPlayerState : INetSerializable {
  public byte PlayerId;
  public PlayerMetadata Metadata;
  public PlayerState PlayerState;
  public NetworkObjectState NetworkObjectState;

  public void Serialize(NetDataWriter writer) {
    writer.Put(PlayerId);
    writer.Put(Metadata);
    writer.Put(PlayerState);
    writer.Put(NetworkObjectState);
  }

  public void Deserialize(NetDataReader reader) {
    PlayerId = reader.GetByte();
    Metadata = reader.Get<PlayerMetadata>();
    PlayerState = reader.Get<PlayerState>();
    NetworkObjectState = reader.Get<NetworkObjectState>();
  }
}

/// Per-frame player state.
/// TODO: See later if the physics state can be merged with network state,
/// once we have non-player networked objects.
public struct PlayerState : INetSerializable {
  public ushort NetworkId;
  public Vector3 Position;
  // TODO: Compress via https://gafferongames.com/post/snapshot_compression/
  public Quaternion Rotation;
  public Vector3 Velocity;
  public bool Grounded;

  public void Serialize(NetDataWriter writer) {
    writer.Put(NetworkId);
    writer.Put(Position);
    writer.Put(Rotation);
    writer.Put(Velocity);
    writer.Put(Grounded);
  }

  public void Deserialize(NetDataReader reader) {
    NetworkId = reader.GetUShort();
    Position = reader.GetVector3();
    Rotation = reader.GetQuaternion();
    Velocity = reader.GetVector3();
    Grounded = reader.GetBool();
  }
}

public struct NetworkObjectState : INetSerializable {
  public ushort NetworkId;

  public void Serialize(NetDataWriter writer) {
    writer.Put(NetworkId);
  }

  public void Deserialize(NetDataReader reader) {
    NetworkId = reader.GetUShort();
  }
}
