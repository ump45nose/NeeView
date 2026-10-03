// Copyright (c) NeeLaboratory. 来源：NeeView/SidePanels/Bookshelf/FolderList/BookSearchProfile.cs。
using NeeLaboratory.IO.Search;
namespace NeeView;

/// <summary>原书籍属性与别名；不注册页面元数据或文件系统专属 Profile。</summary>
public sealed class BookSearchProfile : SearchProfile
{
    /// <summary>沿原 /bookmark、/history 布尔属性，不将虚拟书签文件夹判作已登记书籍。</summary>
    public BookSearchProfile()
    {
        Options.Add(new SearchPropertyProfile("bookmark", BooleanSearchValue.Default));
        Options.Add(new SearchPropertyProfile("history", BooleanSearchValue.Default));
        Alias.Add("/bookmark", new() { "/p.bookmark", "/m.eq", "true" });
        Alias.Add("/history", new() { "/p.history", "/m.eq", "true" });
    }
}
