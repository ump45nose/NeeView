// Copyright (c) NeeLaboratory. 原源码沿用仓库 MIT 许可。
// 来源：NeeView/Config/BookSettingPolicyConfig.cs，基线 c5c398d89；平台改造详见 docs/source-migration.json。
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace NeeView
{

    public partial class BookSettingPolicyConfig : ObservableObject, ICloneable
    {

        private BookSettingPageSelectMode _page = BookSettingPageSelectMode.RestoreOrDefault;

        private BookSettingSelectMode _pageMode = BookSettingSelectMode.RestoreOrDefault;

        private BookSettingSelectMode _bookReadOrder = BookSettingSelectMode.RestoreOrDefault;

        private BookSettingSelectMode _isSupportedDividePage = BookSettingSelectMode.RestoreOrDefault;

        private BookSettingSelectMode _isSupportedSingleFirstPage = BookSettingSelectMode.RestoreOrDefault;

        private BookSettingSelectMode _isSupportedSingleLastPage = BookSettingSelectMode.RestoreOrDefault;

        private BookSettingSelectMode _isSupportedWidePage = BookSettingSelectMode.RestoreOrDefault;

        private BookSettingSelectMode _isRecursiveFolder = BookSettingSelectMode.RestoreOrDefault;

        private BookSettingSelectMode _sortMode = BookSettingSelectMode.RestoreOrDefault;

        private BookSettingSelectMode _autoRotate = BookSettingSelectMode.RestoreOrContinue;

        private BookSettingSelectMode _baseScale = BookSettingSelectMode.RestoreOrDefault;

        private BookSettingSelectMode _effectProfileId = BookSettingSelectMode.Continue;

        public BookSettingPageSelectMode Page
        {
            get { return _page; }
            set { SetProperty(ref _page, value); }
        }

        public BookSettingSelectMode PageMode
        {
            get { return _pageMode; }
            set { SetProperty(ref _pageMode, value); }
        }

        public BookSettingSelectMode BookReadOrder
        {
            get { return _bookReadOrder; }
            set { SetProperty(ref _bookReadOrder, value); }
        }

        public BookSettingSelectMode IsSupportedDividePage
        {
            get { return _isSupportedDividePage; }
            set { SetProperty(ref _isSupportedDividePage, value); }
        }

        public BookSettingSelectMode IsSupportedSingleFirstPage
        {
            get { return _isSupportedSingleFirstPage; }
            set { SetProperty(ref _isSupportedSingleFirstPage, value); }
        }

        public BookSettingSelectMode IsSupportedSingleLastPage
        {
            get { return _isSupportedSingleLastPage; }
            set { SetProperty(ref _isSupportedSingleLastPage, value); }
        }

        public BookSettingSelectMode IsSupportedWidePage
        {
            get { return _isSupportedWidePage; }
            set { SetProperty(ref _isSupportedWidePage, value); }
        }

        public BookSettingSelectMode IsRecursiveFolder
        {
            get { return _isRecursiveFolder; }
            set { SetProperty(ref _isRecursiveFolder, value); }
        }

        public BookSettingSelectMode SortMode
        {
            get { return _sortMode; }
            set { SetProperty(ref _sortMode, value); }
        }

        public BookSettingSelectMode AutoRotate
        {
            get { return _autoRotate; }
            set { SetProperty(ref _autoRotate, value); }
        }

        public BookSettingSelectMode BaseScale
        {
            get { return _baseScale; }
            set { SetProperty(ref _baseScale, value); }
        }

        public BookSettingSelectMode EffectProfileId
        {
            get { return _effectProfileId; }
            set { SetProperty(ref _effectProfileId, value); }
        }

        public object Clone()
        {
            return MemberwiseClone();
        }
    }

}
