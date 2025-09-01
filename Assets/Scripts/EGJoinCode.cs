using System;
using System.Text;

public static class EGJoinCode
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    public static string EncodeRequestIdAndPort(string requestIdHex, ushort port)
    {
        byte[] rid = HexToBytes(requestIdHex);
        if (rid == null || rid.Length < 6)
            throw new ArgumentException("requestId must be >= 12 hex chars.");
        byte[] data = new byte[8];
        Buffer.BlockCopy(rid, 0, data, 0, 6);
        data[6] = (byte)(port >> 8);
        data[7] = (byte)(port & 0xFF);
        return Base58Encode(data);
    }

    public static bool TryDecodeToHostPort(string code, out string host, out ushort port)
    {
        host = null; port = 0;
        try
        {
            byte[] data = Base58Decode(code);
            if (data == null || data.Length != 8) return false;
            byte[] rid = new byte[6];
            Buffer.BlockCopy(data, 0, rid, 0, 6);
            port = (ushort)((data[6] << 8) | data[7]);
            string requestIdHex = BytesToHex(rid);
            host = requestIdHex + ".pr.edgegap.net";
            return true;
        }
        catch { return false; }
    }

    private static byte[] HexToBytes(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
        if (hex.Length % 2 == 1) hex = "0" + hex;
        int len = hex.Length / 2;
        byte[] result = new byte[len];
        for (int i = 0; i < len; i++)
            result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return result;
    }

    private static string BytesToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }

    private static string Base58Encode(byte[] data)
    {
        var digits = new System.Collections.Generic.List<int>();
        foreach (byte b in data)
        {
            int carry = b;
            for (int i = 0; i < digits.Count; i++)
            {
                int val = (digits[i] << 8) + carry;
                digits[i] = val % 58;
                carry = val / 58;
            }
            while (carry > 0)
            {
                digits.Add(carry % 58);
                carry /= 58;
            }
        }
        int zeros = 0; foreach (byte b in data) { if (b == 0) zeros++; else break; }
        var sb = new StringBuilder(zeros + digits.Count);
        for (int i = 0; i < zeros; i++) sb.Append('1');
        for (int i = digits.Count - 1; i >= 0; i--) sb.Append(Alphabet[digits[i]]);
        return sb.ToString();
    }

    private static byte[] Base58Decode(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var bytes = new System.Collections.Generic.List<byte>();
        var ints = new System.Collections.Generic.List<int>();
        foreach (char c in s)
        {
            int val = Alphabet.IndexOf(c);
            if (val < 0) throw new ArgumentException("Invalid base58 char.");
            int carry = val;
            for (int i = 0; i < ints.Count; i++)
            {
                int t = ints[i] * 58 + carry;
                ints[i] = t & 0xFF;
                carry = t >> 8;
            }
            while (carry > 0)
            {
                ints.Add(carry & 0xFF);
                carry >>= 8;
            }
        }
        int zeros = 0; foreach (char c in s) { if (c == '1') zeros++; else break; }
        bytes.AddRange(new byte[zeros]);
        for (int i = ints.Count - 1; i >= 0; i--) bytes.Add((byte)ints[i]);
        return bytes.ToArray();
    }
}
