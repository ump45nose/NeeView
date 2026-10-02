// Copyright (c) NeeLaboratory. 原源码沿用仓库 MIT 许可。
// 来源：NeeView/Config/BookSettingConfig.cs，基线 c5c398d89；平台改造详见 docs/source-migration.json。
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Text.Json.Serialization;

namespace NeeView
{

    public partial class BookSettingConfig : ObservableObject, ICloneable, IBookSetting, IHasAutoRotate
    {

        private string _page = "";

        private PageMode _pageMode = PageMode.SinglePage;

        private PageReadOrder _bookReadOrder = PageReadOrder.RightToLeft;

        private bool _isSupportedDividePage;

        private bool _isSupportedSingleFirstPage;

        private bool _isSupportedSingleLastPage;

        private bool _isSupportedWidePage = true;

        private bool _isRecursiveFolder;

        private PageSortMode _sortMode = PageSortMode.Entry;

        private AutoRotateType _autoRotate;

        private double _baseScale = 1.0;

        private int _effectProfile;


        // ページ
        [JsonIgnore]

        public string Page
        {
            get { return _page; }
            set { SetProperty(ref _page, value); }
        }

        // 1ページ表示 or 2ページ表示

        public PageMode PageMode
        {
            get { return _pageMode; }
            set { SetProperty(ref _pageMode, value); }
        }

        // 右開き or 左開き

        public PageReadOrder BookReadOrder
        {
            get { return _bookReadOrder; }
            set { SetProperty(ref _bookReadOrder, value); }
        }

        // 横長ページ分割 (1ページモード)

        public bool IsSupportedDividePage
        {
            get { return _isSupportedDividePage; }
            set { SetProperty(ref _isSupportedDividePage, value); }
        }

        // 最初のページを単独表示

        public bool IsSupportedSingleFirstPage
        {
            get { return _isSupportedSingleFirstPage; }
            set { SetProperty(ref _isSupportedSingleFirstPage, value); }
        }

        // 最後のページを単独表示

        public bool IsSupportedSingleLastPage
        {
            get { return _isSupportedSingleLastPage; }
            set { SetProperty(ref _isSupportedSingleLastPage, value); }
        }

        // 横長ページを2ページ分とみなす(2ページモード)

        public bool IsSupportedWidePage
        {
            get { return _isSupportedWidePage; }
            set { SetProperty(ref _isSupportedWidePage, value); }
        }

        // フォルダーの再帰

        public bool IsRecursiveFolder
        {
            get { return _isRecursiveFolder; }
            set { SetProperty(ref _isRecursiveFolder, value); }
        }

        // ページ並び順

        public PageSortMode SortMode
        {
            get { return _sortMode; }
            set { SetProperty(ref _sortMode, value); }
        }

        // 自動回転

        public AutoRotateType AutoRotate
        {
            get { return _autoRotate; }
            set { SetProperty(ref _autoRotate, value); }
        }

        // 基底スケール

        public double BaseScale
        {
            get { return _baseScale; }
            set { SetProperty(ref _baseScale, Math.Round(value, 5)); }
        }

        // エフェクトプロファイルID

        public int EffectProfileId
        {
            get { return _effectProfile; }
            set { SetProperty(ref _effectProfile, value); }
        }


        public object Clone()
        {
            return MemberwiseClone();
        }
    }

    public interface IHasAutoRotate
    {
        AutoRotateType AutoRotate { get; set; }
    }
}
