﻿// -----------------------------------------------------------------------
// <copyright company="Fireasy"
//      email="faib920@126.com"
//      qq="55570729">
//   (c) Copyright Fireasy. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Xml;
using Fireasy.Common.Configuration;
using Fireasy.Common.Extensions;

namespace Fireasy.Data.Provider.Configuration
{
    /// <summary>
    /// 表示数据库提供者的配置节。
    /// </summary>
    [ConfigurationSectionStorage("fireasy/dataProviders")]
    public sealed class ProviderConfigurationSection : ConfigurationSection<ProviderConfigurationSetting>
    {
        /// <summary>
        /// 使用配置节点对当前配置进行初始化。
        /// </summary>
        /// <param name="section">对应的配置节点。</param>
        public override void Initialize(XmlNode section)
        {
            InitializeNode(section, "provider", null, ParseProviderSetting);
        }

        private ProviderConfigurationSetting ParseProviderSetting(XmlNode node)
        {
            return new ProviderConfigurationSetting (LoadServices(node))
                {
                    Name = node.GetAttributeValue("name"),
                    Type = Type.GetType(node.GetAttributeValue("type"), false, true)
                };
        }

        /// <summary>
        /// 循环节点provider\service，将服务类添加到配置的服务集合中。
        /// </summary>
        /// <param name="section">provider 节点。</param>
        private IEnumerable<Type> LoadServices(XmlNode section)
        {
            var types = new List<Type>();
            section.EachChildren("service", node =>
                {
                    var type = Type.GetType(node.GetAttributeValue("type"), false, true);
                    if (type != null)
                    {
                        types.Add(type);
                    }
                });
            return types;
        }
    }
}
