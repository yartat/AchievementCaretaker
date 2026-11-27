/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;

namespace AchievementCaretaker.Providers;

// A session whose content cannot be understood looks like being signed out, and the next sign-in
// writes over it. One copy stays so the cause can be seen, until a session is saved again or signed
// out of: it may hold a credential. A file that is only busy is not copied.
internal static class UnreadableFile
{
    private static string CopyOf(string path) => path + ".unreadable";

    public static void KeepAside(string path)
    {
        try
        {
            if (File.Exists(CopyOf(path)) == false)
            {
                File.Copy(path, CopyOf(path));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static void Forget(string path)
    {
        try
        {
            File.Delete(CopyOf(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
