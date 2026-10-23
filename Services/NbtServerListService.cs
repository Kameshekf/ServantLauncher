using System;
using System.IO;
using System.Text;

namespace ServantLauncher.Services
{
    public interface INbtServerListService
    {
        void EnsureServantServersInDirectory(string gameDir);
    }

    public class NbtServerListService : INbtServerListService
    {
        public void EnsureServantServersInDirectory(string gameDir)
        {
            try
            {
                Directory.CreateDirectory(gameDir);
                var serversFile = Path.Combine(gameDir, "servers.dat");

                var servers = new (string Name, string Ip)[]
                {
                    ("Servant [RU]", "play.servant-ru.online"),
                    ("Servant [EU]", "play.servant-eu.online")
                };

                // Check if file already exists and has both
                if (File.Exists(serversFile))
                {
                    var existingBytes = File.ReadAllBytes(serversFile);
                    var str = Encoding.UTF8.GetString(existingBytes);
                    if (str.Contains("play.servant-ru.online", StringComparison.OrdinalIgnoreCase) &&
                        str.Contains("play.servant-eu.online", StringComparison.OrdinalIgnoreCase))
                    {
                        return; // already has both servers
                    }
                }

                // Write NBT servers.dat with both RU and EU servers
                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms);

                // Root Tag_Compound ("")
                bw.Write((byte)10);
                WriteUtfString(bw, "");

                // Tag_List ("servers")
                bw.Write((byte)9);
                WriteUtfString(bw, "servers");

                // List type = Tag_Compound (10), length = servers.Length
                bw.Write((byte)10);
                WriteIntBigEndian(bw, servers.Length);

                foreach (var s in servers)
                {
                    // 1. Tag_String "name"
                    bw.Write((byte)8);
                    WriteUtfString(bw, "name");
                    WriteUtfString(bw, s.Name);

                    // 2. Tag_String "ip"
                    bw.Write((byte)8);
                    WriteUtfString(bw, "ip");
                    WriteUtfString(bw, s.Ip);

                    // 3. Tag_Byte "acceptTextures" = 1
                    bw.Write((byte)1);
                    WriteUtfString(bw, "acceptTextures");
                    bw.Write((byte)1);

                    // End of entry Compound (0)
                    bw.Write((byte)0);
                }

                // End of Root Compound (0)
                bw.Write((byte)0);

                File.WriteAllBytes(serversFile, ms.ToArray());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write servers.dat: {ex.Message}");
            }
        }

        private static void WriteUtfString(BinaryWriter bw, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            WriteShortBigEndian(bw, (short)bytes.Length);
            bw.Write(bytes);
        }

        private static void WriteShortBigEndian(BinaryWriter bw, short value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            bw.Write(bytes);
        }

        private static void WriteIntBigEndian(BinaryWriter bw, int value)
        {
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            bw.Write(bytes);
        }
    }
}
