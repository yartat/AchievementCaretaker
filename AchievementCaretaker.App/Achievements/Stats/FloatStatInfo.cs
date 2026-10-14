/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This project is based on Steam Achievement Manager (SAM)
 * Copyright (c) 2008-2024 Rick (rick 'at' gibbed 'dot' us)
 * https://github.com/gibbed/SteamAchievementManager
 *
 * This is an altered source version of that software, plainly marked as such.
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System.Globalization;

namespace AchievementCaretaker.App.Achievements.Stats
{
    internal sealed class FloatStatInfo : StatInfo
    {
        public float OriginalValue;
        public float FloatValue;

        public override string ValueText
        {
            get => this.FloatValue.ToString(CultureInfo.CurrentCulture);
            set
            {
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var f) == false)
                {
                    this.SetValueError("Invalid value");
                    return;
                }

                if (this.IsProtected == true && this.FloatValue.Equals(f) == false)
                {
                    this.SetValueError("Stat is protected! -- you can't modify it");
                    return;
                }

                this.SetValueError(null);
                if (this.FloatValue.Equals(f) == true)
                {
                    return;
                }
                this.FloatValue = f;
                this.OnPropertyChanged(nameof(this.ValueText));
                this.OnPropertyChanged(nameof(this.IsModified));
            }
        }

        public override bool IsModified => this.FloatValue.Equals(this.OriginalValue) == false;
    }
}
