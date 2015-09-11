﻿////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// <copyright>Copyright 2012-2015 Lawo AG (http://www.lawo.com). All rights reserved.</copyright>
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Lawo.EmberPlus.S101
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Xml;

    using Ember;

    /// <summary>Represents a logger that logs message payloads according to the types passed to the constructor.
    /// </summary>
    /// <threadsafety static="true" instance="false"/>
    public sealed class S101Logger : IS101Logger
    {
        private readonly IEmberConverter converter;
        private readonly XmlWriter xmlLogWriter;
        private readonly Dictionary<string, int> messageCounts = new Dictionary<string, int>();

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>Initializes a new instance of the <see cref="S101Logger"/> class by calling
        /// <see cref="S101Logger(EmberTypeBag, TextWriter, XmlWriterSettings)">S101Logger</see>(
        /// <paramref name="types"/>, <paramref name="logWriter"/>, new <see cref="XmlWriterSettings"/> { Indent = true
        /// }).</summary>
        public S101Logger(EmberTypeBag types, TextWriter logWriter) :
            this(types, logWriter, new XmlWriterSettings { Indent = true })
        {
        }

        /// <summary>Initializes a new instance of the <see cref="S101Logger"/> class.</summary>
        /// <param name="types">The types to pass to the internal <see cref="EmberConverter"/>, which is used to convert
        /// the payload to XML.</param>
        /// <param name="logWriter">The <see cref="TextWriter"/> to write log messages to, will be passed to
        /// <see cref="XmlWriter.Create(TextWriter, XmlWriterSettings)"/>.</param>
        /// <param name="settings">The settings to create the internal <see cref="XmlWriter"/> with, will be passed to
        /// <see cref="XmlWriter.Create(TextWriter, XmlWriterSettings)"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="types"/>, <paramref name="logWriter"/> and/or
        /// <paramref name="settings"/> equal <c>null</c>.</exception>
        public S101Logger(EmberTypeBag types, TextWriter logWriter, XmlWriterSettings settings) :
            this(types, XmlWriter.Create(ValidateLogWriter(logWriter), settings))
        {
        }

        /// <summary>Initializes a new instance of the <see cref="S101Logger"/> class.</summary>
        /// <param name="types">The types to pass to the internal <see cref="EmberConverter"/>, which is used to convert
        /// the payload to XML.</param>
        /// <param name="xmlLogWriter">The <see cref="XmlWriter"/> to write log messages to.</param>
        /// <exception cref="ArgumentNullException"><paramref name="types"/> and/or <paramref name="xmlLogWriter"/>
        /// equal <c>null</c>.</exception>
        public S101Logger(EmberTypeBag types, XmlWriter xmlLogWriter) : this(new EmberConverter(types), xmlLogWriter)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="S101Logger"/> class.</summary>
        /// <param name="converter">The converter to use to convert the payload to XML.</param>
        /// <param name="xmlLogWriter">The <see cref="XmlWriter"/> to write log messages to.</param>
        /// <exception cref="ArgumentNullException"><paramref name="converter"/> and/or <paramref name="xmlLogWriter"/>
        /// equal <c>null</c>.</exception>
        public S101Logger(IEmberConverter converter, XmlWriter xmlLogWriter)
        {
            if (converter == null)
            {
                throw new ArgumentNullException("converter");
            }

            if (xmlLogWriter == null)
            {
                throw new ArgumentNullException("xmlLogWriter");
            }

            this.converter = converter;
            this.xmlLogWriter = xmlLogWriter;
            this.xmlLogWriter.WriteStartDocument();
            this.xmlLogWriter.WriteStartElement(LogNames.Root);
            this.xmlLogWriter.Flush();
        }

        /// <inheritdoc/>
        public EventInfo LogEvent(string eventName)
        {
            return this.LogEvent(eventName, null);
        }

        /// <inheritdoc/>
        public EventInfo LogEvent(string eventName, string data)
        {
            var info = new EventInfo(this.WriteStartEvent(eventName));
            this.xmlLogWriter.WriteString(data);
            this.WriteEndEvent();
            return info;
        }

        /// <inheritdoc/>
        public EventInfo LogData(string type, string direction, byte[] buffer, int index, int count)
        {
            BufferHelper.AssertValidRange(buffer, "buffer", index, "index", count, "count");
            var info = new EventInfo(this.WriteStartEvent(type));
            this.xmlLogWriter.WriteAttributeString(LogNames.Direction, direction);
            this.xmlLogWriter.WriteBinHex(buffer, index, count);
            this.WriteEndEvent();
            return info;
        }

        /// <inheritdoc/>
        public EventInfo LogMessage(string direction, S101Message message, byte[] payload)
        {
            return this.LogMessage(DateTime.UtcNow, direction, message, payload);
        }

        /// <inheritdoc/>
        public EventInfo LogException(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException("exception");
            }

            var info = new EventInfo(this.WriteStartEvent(LogNames.Exception));
            this.xmlLogWriter.WriteString(exception.ToString());
            this.WriteEndEvent();
            return info;
        }

        /// <summary>Releases all resources used by the current instance of the <see cref="S101Logger"/> class.
        /// </summary>
        public void Dispose()
        {
            this.xmlLogWriter.Dispose();
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal EventInfo LogMessage(DateTime timeUtc, string direction, S101Message message, byte[] payload)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            if (direction == null)
            {
                throw new ArgumentNullException("direction");
            }

            this.WriteStartEvent(LogNames.Message, timeUtc);
            int number;
            this.messageCounts.TryGetValue(direction, out number);
            this.messageCounts[direction] = ++number;

            this.xmlLogWriter.WriteAttributeString(LogNames.Direction, direction);
            this.xmlLogWriter.WriteAttributeString(LogNames.Number, number.ToString(CultureInfo.InvariantCulture));
            this.xmlLogWriter.WriteElementString(
                LogNames.Slot, message.Slot.ToString("X2", CultureInfo.InvariantCulture));
            this.xmlLogWriter.WriteElementString(LogNames.Command, message.Command.ToString());
            this.xmlLogWriter.WriteStartElement(LogNames.Payload);

            if (payload != null)
            {
                this.converter.ToXml(payload, this.xmlLogWriter);
            }

            this.xmlLogWriter.WriteEndElement();
            this.WriteEndEvent();
            return new EventInfo(timeUtc, number);
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        private DateTime WriteStartEvent(string type)
        {
            var timeUtc = DateTime.UtcNow;
            this.WriteStartEvent(type, timeUtc);
            return timeUtc;
        }

        private void WriteStartEvent(string type, DateTime timeUtc)
        {
            this.xmlLogWriter.WriteStartElement(LogNames.Event);
            this.xmlLogWriter.WriteAttributeString(LogNames.Type, type);
            this.xmlLogWriter.WriteAttributeString(
                LogNames.Time, timeUtc.ToString("HH':'mm':'ss'.'ff", CultureInfo.InvariantCulture));
        }

        private void WriteEndEvent()
        {
            this.xmlLogWriter.WriteEndElement();
            this.xmlLogWriter.Flush();
        }

        private static TextWriter ValidateLogWriter(TextWriter logWriter)
        {
            if (logWriter == null)
            {
                throw new ArgumentNullException("logWriter");
            }

            return logWriter;
        }
    }
}
