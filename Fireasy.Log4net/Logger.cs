﻿using Fireasy.Common.Logging;
using log4net;
// -----------------------------------------------------------------------
// <copyright company="Fireasy"
//      email="faib920@126.com"
//      qq="55570729">
//   (c) Copyright Fireasy. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------
using System;
using System.Reflection;

namespace Fireasy.Log4net
{
    /// <summary>
    /// 基于 log4net 的日志管理器。
    /// </summary>
    public class Logger : ILogger
    {
        private ILog log;

        public Logger(string name)
        {
            log = string.IsNullOrEmpty(name) ? 
                LogManager.GetLogger(typeof(Logger)) : 
                LogManager.GetLogger(name);
        }

        /// <summary>
        /// 记录错误信息到日志。
        /// </summary>
        /// <param name="message">要记录的信息。</param>
        /// <param name="exception">异常对象。</param>
        public void Error(object message, Exception exception = null)
        {
            log.Error(message, exception);
        }

        /// <summary>
        /// 记录一般的信息到日志。
        /// </summary>
        /// <param name="message">要记录的信息。</param>
        /// <param name="exception">异常对象。</param>
        public void Info(object message, Exception exception = null)
        {
            log.Info(message, exception);
        }

        /// <summary>
        /// 记录警告信息到日志。
        /// </summary>
        /// <param name="message">要记录的信息。</param>
        /// <param name="exception">异常对象。</param>
        public void Warn(object message, Exception exception = null)
        {
            log.Warn(message, exception);
        }

        /// <summary>
        /// 记录调试信息到日志。
        /// </summary>
        /// <param name="message">要记录的信息。</param>
        /// <param name="exception">异常对象。</param>
        public void Debug(object message, Exception exception = null)
        {
            log.Debug(message, exception);
        }

        /// <summary>
        /// 记录致命信息到日志。
        /// </summary>
        /// <param name="message">要记录的信息。</param>
        /// <param name="exception">异常对象。</param>
        public void Fatal(object message, Exception exception = null)
        {
            log.Fatal(message, exception);
        }
    }
}
