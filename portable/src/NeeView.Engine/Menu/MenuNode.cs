// Copyright (c) NeeLaboratory. 来源：NeeView/Menu/MenuNode.cs，保留原树结构。
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NeeView
{
    /// <summary>
    /// ContextMenu 保存用データ構造
    /// </summary>
    public class MenuNode
    {
        /// <summary>创建空菜单节点，供原 JSON 恢复。</summary>
        public MenuNode()
        {
        }

        /// <summary>创建指定名称、类型和原命令标识的节点。</summary>
        public MenuNode(string? name, MenuElementType menuElementType, string? commandName)
        {
            Name = name;
            MenuElementType = menuElementType;
            CommandName = commandName;
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }

        public MenuElementType MenuElementType { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CommandName { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<MenuNode>? Children { get; set; }


        /// <summary>按原菜单顺序递归列出节点，包括未迁移命令。</summary>
        public IEnumerable<MenuNode> GetEnumerator()
        {
            yield return this;

            if (Children != null)
            {
                foreach (var child in Children)
                {
                    foreach (var subChild in child.GetEnumerator())
                    {
                        yield return subChild;
                    }
                }
            }
        }
    }
}
