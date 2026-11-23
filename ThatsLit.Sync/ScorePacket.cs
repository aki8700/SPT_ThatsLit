using Fika.Core.Networking.LiteNetLib.Utils;

namespace ThatsLit.Sync
{
    public struct ScorePacket : INetSerializable
    {
        public const byte CurrentVersion = 2;

        public byte version;
        public int netId;
        public float score, ambienceScore, litScoreFactor, baseAmbienceScore;

        public ScorePacket(int netId, float score, float ambScore, float litScoreFactor, float baseAmbienceScore) : this()
        {
            this.version = CurrentVersion;
            this.netId = netId;
            this.score = score;
            this.ambienceScore = ambScore;
            this.litScoreFactor = litScoreFactor;
            this.baseAmbienceScore = baseAmbienceScore;
        }

        public void Deserialize(NetDataReader reader)
        {
            version = reader.GetByte();
            netId = reader.GetInt();
            score = reader.GetFloat();
            ambienceScore = reader.GetFloat();
            if (version >= 2)
            {
                litScoreFactor = reader.GetFloat();
                baseAmbienceScore = reader.GetFloat();
            }
            else
            {
                litScoreFactor = 0f;
                baseAmbienceScore = ambienceScore;
            }
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.PutUnmanaged(version);
            writer.PutUnmanaged(netId);
            writer.PutUnmanaged(score);
            writer.PutUnmanaged(ambienceScore);
            writer.PutUnmanaged(litScoreFactor);
            writer.PutUnmanaged(baseAmbienceScore);
        }
    }
}
