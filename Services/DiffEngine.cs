using System;
using System.Collections.Generic;

namespace Notifier.Services
{
    public enum DiffType
    {
        Unchanged,
        Added,
        Deleted
    }

    public class DiffLine
    {
        public DiffType Type { get; set; }
        public string Text { get; set; } = string.Empty;
        public int? OldLineNumber { get; set; }
        public int? NewLineNumber { get; set; }

        public string LinePrefix => Type switch
        {
            DiffType.Added => "+",
            DiffType.Deleted => "-",
            _ => " "
        };

        public string BackgroundColor => Type switch
        {
            DiffType.Added => "#2610B981",  // Translucent green (15% opacity)
            DiffType.Deleted => "#26EF4444", // Translucent red (15% opacity)
            _ => "#00000000"                // Transparent
        };

        public string ForegroundColor => Type switch
        {
            DiffType.Added => "#10B981",  // Solid Green for text/prefix accent
            DiffType.Deleted => "#EF4444", // Solid Red for text/prefix accent
            _ => "#888888"                // Dim gray for line numbers/unchanged prefix
        };

        public string DisplayOldLine => OldLineNumber?.ToString() ?? string.Empty;
        public string DisplayNewLine => NewLineNumber?.ToString() ?? string.Empty;
    }

    public static class DiffEngine
    {
        public static List<DiffLine> ComputeDiff(string oldText, string newText)
        {
            string[] oldLines = (oldText ?? string.Empty).Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            string[] newLines = (newText ?? string.Empty).Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            int m = oldLines.Length;
            int n = newLines.Length;

            // DP table to find LCS
            int[,] dp = new int[m + 1, n + 1];

            for (int i = 1; i <= m; i++)
            {
                for (int j = 1; j <= n; j++)
                {
                    if (oldLines[i - 1] == newLines[j - 1])
                    {
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    }
                    else
                    {
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                    }
                }
            }

            List<DiffLine> result = new List<DiffLine>();
            int x = m, y = n;

            while (x > 0 || y > 0)
            {
                if (x > 0 && y > 0 && oldLines[x - 1] == newLines[y - 1])
                {
                    result.Add(new DiffLine
                    {
                        Type = DiffType.Unchanged,
                        Text = oldLines[x - 1],
                        OldLineNumber = x,
                        NewLineNumber = y
                    });
                    x--;
                    y--;
                }
                else if (y > 0 && (x == 0 || dp[x, y - 1] >= dp[x - 1, y]))
                {
                    result.Add(new DiffLine
                    {
                        Type = DiffType.Added,
                        Text = newLines[y - 1],
                        NewLineNumber = y
                    });
                    y--;
                }
                else
                {
                    result.Add(new DiffLine
                    {
                        Type = DiffType.Deleted,
                        Text = oldLines[x - 1],
                        OldLineNumber = x
                    });
                    x--;
                }
            }

            result.Reverse();
            return result;
        }
    }
}
