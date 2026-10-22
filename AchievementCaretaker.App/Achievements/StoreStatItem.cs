/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using AchievementCaretaker.Providers;
using AchievementCaretaker.Services;

namespace AchievementCaretaker.App.Achievements
{
    /// <summary>
    /// One statistic in the editor: what the store reported, and what the user has typed since.
    /// A value the store would refuse is not kept; the box shows the reason instead.
    /// </summary>
    internal sealed class StoreStatItem : ObservableObject, INotifyDataErrorInfo
    {
        private double _Value;
        private string _ValueError;

        public StoreStatItem(StoreStat source)
        {
            this.Source = source;
            this._Value = source.Value;
        }

        public StoreStat Source { get; }

        public string Id => this.Source.Id;

        public string Name => string.IsNullOrEmpty(this.Source.DisplayName) == true ? this.Source.Id : this.Source.DisplayName;

        public bool IsProtected => this.Source.Protected;

        /// <summary>The value on screen: the store's, unless the user has changed it.</summary>
        public double Value => this._Value;

        public bool IsModified => this._Value != this.Source.Value;

        /// <summary>
        /// What the box shows and takes. Parsing and the protected / range checks are the
        /// service's, so the rules are the same wherever a value is typed.
        /// </summary>
        public string ValueText
        {
            get => this.Source.Kind == StoreStatKind.Integer
                ? ((int)this._Value).ToString(CultureInfo.CurrentCulture)
                : ((float)this._Value).ToString(CultureInfo.CurrentCulture);
            set
            {
                if (StatValueParser.TryParse(this.Source, value, CultureInfo.CurrentCulture, out var parsed, out var error) == false)
                {
                    this.SetValueError(error);
                    return;
                }

                this.SetValueError(null);
                if (parsed == this._Value)
                {
                    return;
                }
                this._Value = parsed;
                this.OnPropertyChanged(nameof(this.ValueText));
                this.OnPropertyChanged(nameof(this.IsModified));
            }
        }

        /// <summary>Back to what the store reported.</summary>
        public void Discard()
        {
            this._Value = this.Source.Value;
            this.SetValueError(null);
            this.OnPropertyChanged(nameof(this.ValueText));
            this.OnPropertyChanged(nameof(this.IsModified));
        }

        /// <summary>The limits and flags, in one cell.</summary>
        public string RangeText
        {
            get
            {
                var text = this.Source.Kind == StoreStatKind.Integer
                    ? $"{this.Source.Min:0} .. {this.Source.Max:0}"
                    : $"{this.Source.Min:G4} .. {this.Source.Max:G4}";
                if (this.Source.IncrementOnly == true)
                {
                    text += " · only grows";
                }
                if (this.Source.Protected == true)
                {
                    text += " · protected";
                }
                return text;
            }
        }

        #region INotifyDataErrorInfo

        public bool HasErrors => this._ValueError != null;

        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        public IEnumerable GetErrors(string propertyName) =>
            propertyName == nameof(this.ValueText) && this._ValueError != null
                ? new[] { this._ValueError }
                : Array.Empty<string>();

        private void SetValueError(string error)
        {
            if (this._ValueError == error)
            {
                return;
            }
            this._ValueError = error;
            this.ErrorsChanged?.Invoke(this, new(nameof(this.ValueText)));
            this.OnPropertyChanged(nameof(this.HasErrors));
        }

        #endregion
    }
}
