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
using AchievementCaretaker.Providers.Models;

namespace AchievementCaretaker.App.Achievements;

/// <summary>
/// One statistic in the editor: what the store reported, and what the user has typed since.
/// A value the store would refuse is not kept; the box shows the reason instead.
/// </summary>
internal sealed class StoreStatItem : ObservableObject, INotifyDataErrorInfo
{
    private double _value;
    private string? _valueError;

    public StoreStatItem(StoreStat source)
    {
        Source = source;
        _value = source.Value;
    }

    public StoreStat Source { get; }

    public string Id => Source.Id;

    public string Name => string.IsNullOrEmpty(Source.DisplayName) == true ? Source.Id : Source.DisplayName;

    public bool IsProtected => Source.Protected;

    /// <summary>The value on screen: the store's, unless the user has changed it.</summary>
    public double Value => _value;

    public bool IsModified => _value != Source.Value;

    /// <summary>
    /// What the box shows and takes. Parsing and the protected / range checks are the
    /// service's, so the rules are the same wherever a value is typed.
    /// </summary>
    public string ValueText
    {
        get => Source.Kind == StoreStatKind.Integer
            ? ((int)_value).ToString(CultureInfo.CurrentCulture)
            : ((float)_value).ToString(CultureInfo.CurrentCulture);
        set
        {
            if (StatValueParser.TryParse(Source, value, CultureInfo.CurrentCulture, out var parsed, out var error) == false)
            {
                SetValueError(error);
                return;
            }

            SetValueError(null);
            if (parsed == _value)
            {
                return;
            }
            _value = parsed;
            OnPropertyChanged(nameof(ValueText));
            OnPropertyChanged(nameof(IsModified));
        }
    }

    /// <summary>Back to what the store reported.</summary>
    public void Discard()
    {
        _value = Source.Value;
        SetValueError(null);
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(IsModified));
    }

    /// <summary>The limits and flags, in one cell.</summary>
    public string RangeText
    {
        get
        {
            var text = Source.Kind == StoreStatKind.Integer
                ? $"{Source.Min:0} .. {Source.Max:0}"
                : $"{Source.Min:G4} .. {Source.Max:G4}";
            if (Source.IncrementOnly == true)
            {
                text += " · only grows";
            }
            if (Source.Protected == true)
            {
                text += " · protected";
            }
            return text;
        }
    }

    #region INotifyDataErrorInfo

    public bool HasErrors => _valueError != null;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName) =>
        propertyName == nameof(ValueText) && _valueError != null
            ? new[] { _valueError }
            : Array.Empty<string>();

    private void SetValueError(string? error)
    {
        if (_valueError == error)
        {
            return;
        }
        _valueError = error;
        ErrorsChanged?.Invoke(this, new(nameof(ValueText)));
        OnPropertyChanged(nameof(HasErrors));
    }

    #endregion
}
