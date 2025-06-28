// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using findneedle;
using FindNeedlePluginLib;

namespace Microsoft.Toolkit.Uwp.SampleApp.Data;

public class SearchSourceDataItem : INotifyDataErrorInfo, IComparable
{
    private readonly Dictionary<string, List<string>> _errors = new();

   
    private string _parentMountain;
    private readonly ISearchResult _ret;


    public SearchSourceDataItem(ISearchResult yay)
    {
        _ret = yay;
    }
    public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

    public string Provider
    {
        get => _ret.GetSource();

        set
        {

        }
    }

    public string Time
    {
        get => _ret.GetLogTime().ToString();

        set
        {

        }
    }

    public string TaskName
    {
        get => _ret.GetTaskName();

        set
        {

        }
    }


    public string Message
    {
        get => _ret.GetMessage();

        set
        {
        }
    }

    public string Parent_mountain
    {
        get => _parentMountain;

        set
        {
            if (_parentMountain != value)
            {
                _parentMountain = value;

                var isParentValid = !_errors.Keys.Contains("Parent_mountain");
                if (_parentMountain == string.Empty && isParentValid)
                {
                    List<string> errors = new List<string>();
                    errors.Add("Parent_mountain name cannot be empty");
                    _errors.Add("Parent_mountain", errors);
                    this.ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs("Parent_mountain"));
                }
                else if (_parentMountain != string.Empty && !isParentValid)
                {
                    _errors.Remove("Parent_mountain");
                    this.ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs("Parent_mountain"));
                }
            }
        }
    }

    public string Coordinates
    {
        get; set;
    }

    public uint Prominence
    {
        get; set;
    }

    public uint First_ascent
    {
        get; set;
    }

    public string Ascents
    {
        get; set;
    }

    bool INotifyDataErrorInfo.HasErrors => _errors.Keys.Count > 0;

    IEnumerable INotifyDataErrorInfo.GetErrors(string propertyName)
    {
        propertyName ??= string.Empty;

        if (_errors.Keys.Contains(propertyName))
        {
            return _errors[propertyName];
        }
        else
        {
            return null;
        }
    }

    int IComparable.CompareTo(object obj)
    {
        var lnCompare = Message.CompareTo((obj as SearchSourceDataItem).Message);

        if (lnCompare == 0)
        {
            return Parent_mountain.CompareTo((obj as SearchSourceDataItem).Parent_mountain);
        }
        else
        {
            return lnCompare;
        }
    }
}