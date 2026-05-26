using System;

namespace SayusGagExtender;

public static class Base64Url
{
    public static string Encode(byte[] data)
    {
        return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[] Decode(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');

        switch (value.Length % 4)
        {
            case 2:
                value += "==";
                break;
            case 3:
                value += "=";
                break;
        }

        return Convert.FromBase64String(value);
    }
}
