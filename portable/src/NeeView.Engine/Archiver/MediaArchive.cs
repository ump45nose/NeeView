// Copyright (c) NeeView. Original single-entry MediaArchive relationship, MIT license.
namespace NeeView;
/// <summary>直接媒体书只有一个完整视频条目，不按PageSeconds预切页。</summary>
public abstract class MediaArchive(string path) : Archive(path);
