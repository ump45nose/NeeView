// Copyright (c) NeeLaboratory. MIT. 原Information配置；列宽保留原JSON字符串，显示端解释单位。
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace NeeView
{
    public partial class InformationConfig : ObservableObject
    {
        private static readonly string _defaultDateTimeFormat = HelpText.GetString("Information.DateFormat");
        private static readonly string _defaultMapProgramFormat = @"https://www.google.com/maps/place/{Lat}+{Lon}/";

        private string _propertyHeaderWidth = "128";
        private string? _dateTimeFormat = null;
        private string? _mapProgramFormat;

        private readonly Dictionary<InformationGroup, bool> _groupVisibilityMap = new()
        {
            [InformationGroup.File] = true,
            [InformationGroup.Image] = true,
            [InformationGroup.Description] = true,
            [InformationGroup.Origin] = true,
            [InformationGroup.Camera] = true,
            [InformationGroup.AdvancedPhoto] = true,
            [InformationGroup.Gps] = true,
            [InformationGroup.Extras] = false,
        };


        private bool SetVisibleGroup(InformationGroup group, bool isVisible, [CallerMemberName] string? propertyName = null)
        {
            if (_groupVisibilityMap[group] == isVisible) return false;

            _groupVisibilityMap[group] = isVisible;
            OnPropertyChanged(propertyName);
            return true;
        }

        public bool IsVisibleGroup(InformationGroup group)
        {
            return _groupVisibilityMap[group];
        }


        [JsonIgnore]
        public string DateTimeFormat
        {
            get { return _dateTimeFormat ?? _defaultDateTimeFormat; }
            set { SetProperty(ref _dateTimeFormat, (string.IsNullOrWhiteSpace(value) || value == _defaultDateTimeFormat) ? null : value); }
        }

        [JsonPropertyName(nameof(DateTimeFormat))]
        public string? DateTimeFormatRaw
        {
            get { return _dateTimeFormat; }
            set { _dateTimeFormat = value; }
        }

        [JsonIgnore]
        public string MapProgramFormat
        {
            get { return _mapProgramFormat ?? _defaultMapProgramFormat; }
            set { SetProperty(ref _mapProgramFormat, (string.IsNullOrWhiteSpace(value) || value == _defaultMapProgramFormat) ? null : value); }
        }

        [JsonPropertyName(nameof(MapProgramFormat))]
        public string? MapProgramFormatRaw
        {
            get { return _mapProgramFormat; }
            set { _mapProgramFormat = value; }
        }

        public bool IsVisibleFile
        {
            get { return IsVisibleGroup(InformationGroup.File); }
            set { SetVisibleGroup(InformationGroup.File, value); }
        }

        public bool IsVisibleImage
        {
            get { return IsVisibleGroup(InformationGroup.Image); }
            set { SetVisibleGroup(InformationGroup.Image, value); }
        }

        public bool IsVisibleDescription
        {
            get { return IsVisibleGroup(InformationGroup.Description); }
            set { SetVisibleGroup(InformationGroup.Description, value); }
        }

        public bool IsVisibleOrigin
        {
            get { return IsVisibleGroup(InformationGroup.Origin); }
            set { SetVisibleGroup(InformationGroup.Origin, value); }
        }

        public bool IsVisibleCamera
        {
            get { return IsVisibleGroup(InformationGroup.Camera); }
            set { SetVisibleGroup(InformationGroup.Camera, value); }
        }

        public bool IsVisibleAdvancedPhoto
        {
            get { return IsVisibleGroup(InformationGroup.AdvancedPhoto); }
            set { SetVisibleGroup(InformationGroup.AdvancedPhoto, value); }
        }

        public bool IsVisibleGps
        {
            get { return IsVisibleGroup(InformationGroup.Gps); }
            set { SetVisibleGroup(InformationGroup.Gps, value); }
        }

        public bool IsVisibleExtras
        {
            get { return IsVisibleGroup(InformationGroup.Extras); }
            set { SetVisibleGroup(InformationGroup.Extras, value); }
        }

        #region HiddenParameters

        [JsonIgnore]
        public ReadOnlyDictionary<InformationGroup, bool> GroupVisibilityMap => new(_groupVisibilityMap);

        public string PropertyHeaderWidth
        {
            get { return _propertyHeaderWidth; }
            set { SetProperty(ref _propertyHeaderWidth, value); }
        }

        #endregion HiddenParameters
    }
}
