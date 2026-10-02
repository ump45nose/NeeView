// Copyright (c) NeeLaboratory. 来源：NeeView/Bookamrk/BookmarkCollection.cs:BookmarkNode。
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
namespace NeeView;
    public class BookmarkNode : ObservableObject
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get => _name; set { if (SetProperty(ref _name, value)) OnPropertyChanged(nameof(DisplayName)); } }
        private string? _name;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Path { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Color { get => _color; set => SetProperty(ref _color, value); }
        private string? _color;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public DateTime EntryTime { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Page { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Props { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Invalid { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ObservableCollection<BookmarkNode>? Children { get; set; }

        // 保留后续版本和暂未迁移的字段，保存时不静默丢失。
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }

        [JsonIgnore]
        public bool IsFolder => Children != null;

        [JsonIgnore]
        public string DisplayName => Name ?? (IsFolder ? "书签文件夹" : System.IO.Path.GetFileName(Path?.TrimEnd(System.IO.Path.DirectorySeparatorChar)) ?? "书签");

        /// <summary>按原书签树顺序递归遍历；文件夹与书籍保持层次关系。</summary>
        public IEnumerable<BookmarkNode> Walk()
        {
            yield return this;

            if (Children != null)
            {
                foreach (var child in Children)
                {
                    foreach (var subChild in child.Walk())
                    {
                        yield return subChild;
                    }
                }
            }
        }
    }
