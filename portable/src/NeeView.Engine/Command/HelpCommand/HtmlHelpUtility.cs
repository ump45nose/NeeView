// Copyright (c) NeeLaboratory. 原Help HTML头尾和CSS，嵌入资源替换WPF Application.GetResourceStream。
using System.Net;
namespace NeeView;

public static class HtmlHelpUtility
{
    /// <summary>创建独立UTF8文档，样式从固定原CSS读取；标题按文本转义。</summary>
    /// <param name="title">原帮助标题。</param><returns>doctype及完整head。</returns>
    public static string CreateHeader(string title)
    {
        using var stream = typeof(HtmlHelpUtility).Assembly.GetManifestResourceStream("NeeView.Command.HelpCommand.Style.css")
            ?? throw new InvalidDataException("缺少原帮助样式。");
        using var reader = new StreamReader(stream);
        return "<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\"><style>" + reader.ReadToEnd()
            + "</style><title>" + WebUtility.HtmlEncode(title) + "</title></head>";
    }
    /// <summary>关闭独立HTML文档；body由对应原生成器管理。</summary>
    public static string CreateFooter() => "</html>";
}
