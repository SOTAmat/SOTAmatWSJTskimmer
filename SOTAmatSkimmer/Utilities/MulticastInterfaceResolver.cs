using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace SOTAmatSkimmer.Utilities
{
    internal static class MulticastInterfaceResolver
    {
        public static IPAddress? ResolveIPv4Address(string selector)
        {
            if (string.IsNullOrWhiteSpace(selector))
            {
                return null;
            }

            if (IPAddress.TryParse(selector, out IPAddress? parsedAddress))
            {
                if (parsedAddress.AddressFamily != AddressFamily.InterNetwork)
                {
                    throw new InvalidOperationException($"Multicast interface '{selector}' is not an IPv4 address.");
                }

                return parsedAddress;
            }

            InterfaceAddressMatch? match = FindInterfaceAddress(selector);
            if (match is null)
            {
                throw new InvalidOperationException(
                    $"Unable to resolve multicast interface '{selector}' to an IPv4 address.{Environment.NewLine}" +
                    $"Available IPv4 interfaces:{Environment.NewLine}{DescribeAvailableIPv4Interfaces()}");
            }

            return match.Address;
        }

        public static string DescribeSelection(string selector)
        {
            if (string.IsNullOrWhiteSpace(selector))
            {
                return "default interface";
            }

            InterfaceAddressMatch? match = FindInterfaceAddress(selector);
            if (match is null)
            {
                IPAddress? fallback = ResolveIPv4Address(selector);
                return fallback is null ? "default interface" : fallback.ToString();
            }

            string indexText = match.Index.HasValue ? $"index {match.Index.Value}" : "index unavailable";
            return $"{match.Interface.Name} [{match.Address}, {indexText}]";
        }

        public static string DescribeAvailableIPv4Interfaces()
        {
            StringBuilder sb = new();
            foreach (InterfaceAddressMatch match in EnumerateIPv4Addresses())
            {
                string indexText = match.Index.HasValue ? match.Index.Value.ToString() : "n/a";
                sb.Append("  - ");
                sb.Append(match.Interface.Name);
                if (!string.Equals(match.Interface.Description, match.Interface.Name, StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(" (");
                    sb.Append(match.Interface.Description);
                    sb.Append(')');
                }

                sb.Append(": ");
                sb.Append(match.Address);
                sb.Append(", index ");
                sb.Append(indexText);
                sb.Append(", status ");
                sb.Append(match.Interface.OperationalStatus);
                sb.AppendLine();
            }

            return sb.Length == 0 ? "  - none" : sb.ToString().TrimEnd();
        }

        private static InterfaceAddressMatch? FindInterfaceAddress(string selector)
        {
            bool isIndex = int.TryParse(selector, out int parsedIndex);
            string normalizedSelector = NormalizeSelector(selector);
            foreach (InterfaceAddressMatch match in EnumerateIPv4Addresses())
            {
                if (isIndex && match.Index == parsedIndex)
                {
                    return match;
                }

                if (string.Equals(match.Interface.Name, selector, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(match.Interface.Description, selector, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(match.Interface.Id, selector, StringComparison.OrdinalIgnoreCase))
                {
                    return match;
                }

                string normalizedName = NormalizeSelector(match.Interface.Name);
                string normalizedDescription = NormalizeSelector(match.Interface.Description);
                if (normalizedName == normalizedSelector || normalizedDescription == normalizedSelector)
                {
                    return match;
                }

                if (normalizedSelector.StartsWith("loopback", StringComparison.OrdinalIgnoreCase) &&
                    (match.Interface.NetworkInterfaceType == NetworkInterfaceType.Loopback || IPAddress.IsLoopback(match.Address)))
                {
                    return match;
                }
            }

            return null;
        }

        private static string NormalizeSelector(string value)
        {
            StringBuilder sb = new(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            return sb.ToString();
        }

        private static IEnumerable<InterfaceAddressMatch> EnumerateIPv4Addresses()
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                IPInterfaceProperties properties;
                try
                {
                    properties = nic.GetIPProperties();
                }
                catch
                {
                    continue;
                }

                int? index = null;
                try
                {
                    index = properties.GetIPv4Properties()?.Index;
                }
                catch
                {
                    // ignore interfaces that cannot provide an IPv4 index
                }

                foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        yield return new InterfaceAddressMatch(nic, unicast.Address, index);
                    }
                }
            }
        }

        private sealed record InterfaceAddressMatch(NetworkInterface Interface, IPAddress Address, int? Index);
    }
}
