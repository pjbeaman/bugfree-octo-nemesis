﻿//
// SimplePostHandler.cs
//
// Author:
//       Martin Baulig <martin.baulig@xamarin.com>
//
// Copyright (c) 2014 Xamarin Inc. (http://www.xamarin.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Globalization;
using System.Collections.Generic;

namespace Xamarin.WebTests.Server
{
	using Framework;

	public class PostHandler : Handler
	{
		string body;
		int? readChunkSize;
		int? readChunkMinDelay, readChunkMaxDelay;
		int? writeChunkSize;
		int? writeChunkMinDelay, writeChunkMaxDelay;
		bool? allowWriteBuffering;
		TransferMode mode = TransferMode.Default;

		public TransferMode Mode {
			get { return mode; }
			set {
				WantToModify ();
				mode = value;
			}
		}

		public string Body {
			get {
				return body;
			}
			set {
				WantToModify ();
				body = value;
			}
		}

		public int? ReadChunkSize {
			get {
				return readChunkSize;
			}
			set {
				WantToModify ();
				readChunkSize = value;
			}
		}

		public int? ReadChunkMinDelay {
			get {
				return readChunkMinDelay;
			}
			set {
				WantToModify ();
				readChunkMinDelay = value;
			}
		}

		public int? ReadChunkMaxDelay {
			get {
				return readChunkMaxDelay;
			}
			set {
				WantToModify ();
				readChunkMaxDelay = value;
			}
		}

		public bool? AllowWriteStreamBuffering {
			get {
				return allowWriteBuffering;
			}
			set {
				WantToModify ();
				allowWriteBuffering = value;
			}
		}

		public int? WriteChunkSize {
			get {
				return writeChunkSize;
			}
			set {
				WantToModify ();
				writeChunkSize = value;
			}
		}

		public int? WriteChunkMinDelay {
			get {
				return writeChunkMinDelay;
			}
			set {
				WantToModify ();
				writeChunkMinDelay = value;
			}
		}

		public int? WriteChunkMaxDelay {
			get {
				return writeChunkMaxDelay;
			}
			set {
				WantToModify ();
				writeChunkMaxDelay = value;
			}
		}

		internal bool HandlePostRequest (Connection connection)
		{
			return HandlePostRequest (connection, RequestFlags.None);
		}

		bool HandlePostRequest (Connection connection, RequestFlags effectiveFlags)
		{
			Debug (0, "HANDLE POST", connection.Path, connection.Method, effectiveFlags);

			if ((effectiveFlags & RequestFlags.RedirectedAsGet) != 0) {
				if (!connection.Method.Equals ("GET")) {
					WriteError (connection, "Wrong method: {0}", connection.Method);
					return false;
				}
			} else {
				if (!connection.Method.Equals ("POST") && !connection.Method.Equals ("PUT")) {
					WriteError (connection, "Wrong method: {0}", connection.Method);
					return false;
				}
			}

			return CheckTransferMode (connection, effectiveFlags);
		}

		protected override bool DoHandleRequest (Connection connection)
		{
			if (!HandlePostRequest (connection, Flags))
				return false;

			WriteSuccess (connection);
			return true;
		}

		bool CheckTransferMode (Connection connection, RequestFlags effectiveFlags)
		{
			var haveContentLength = connection.Headers.ContainsKey ("Content-Length");
			var haveTransferEncoding = connection.Headers.ContainsKey ("Transfer-Encoding");

			if ((effectiveFlags & RequestFlags.RedirectedAsGet) != 0) {
				if (haveContentLength) {
					WriteError (connection, "Content-Length header not allowed");
					return false;
				}

				if (haveTransferEncoding) {
					WriteError (connection, "Transfer-Encoding header not allowed");
					return false;
				}

				return true;
			}

			switch (Mode) {
			case TransferMode.Default:
				if (Body != null) {
					if (!haveContentLength) {
						WriteError (connection, "Missing Content-Length");
						return false;
					}

					return ReadStaticBody (connection);
				} else {
					if (haveContentLength) {
						WriteError (connection, "Content-Length header not allowed");
						return false;
					}

					return true;
				}

			case TransferMode.ContentLength:
				if (!haveContentLength) {
					WriteError (connection, "Missing Content-Length");
					return false;
				}

				if (haveTransferEncoding) {
					WriteError (connection, "Transfer-Encoding header not allowed");
					return false;
				}

				return ReadStaticBody (connection);

			case TransferMode.Chunked:
				if (haveContentLength) {
					WriteError (connection, "Content-Length header not allowed");
					return false;
				}

				if (!haveTransferEncoding) {
					WriteError (connection, "Missing Transfer-Encoding header");
					return false;
				}

				var transferEncoding = connection.Headers ["Transfer-Encoding"];
				if (!string.Equals (transferEncoding, "chunked", StringComparison.InvariantCultureIgnoreCase)) {
					WriteError (connection, "Invalid Transfer-Encoding header: '{0}'", transferEncoding);
					return false;
				}

				var body = ReadChunkedBody (connection);
				Console.WriteLine ("CHUNKED BODY: |{0}|", body);

				return true;

			default:
				WriteError (connection, "Unknown TransferMode: '{0}'", Mode);
				return false;
			}
		}

		bool ReadStaticBody (Connection connection)
		{
			var length = int.Parse (connection.Headers ["Content-Length"]);

			var chunkSize = ReadChunkSize ?? length;
			var minDelay = ReadChunkMinDelay ?? 0;
			var maxDelay = ReadChunkMaxDelay ?? 0;

			var random = new Random ();
			var delayRange = maxDelay - minDelay;

			var buffer = new char [length];
			int offset = 0;
			while (offset < length) {
				int delay = minDelay + random.Next (delayRange);
				Thread.Sleep (delay);

				var size = Math.Min (length - offset, chunkSize);
				int ret = connection.RequestReader.Read (buffer, offset, size);
				if (ret <= 0) {
					WriteError (connection, "Failed to read body.");
					return false;
				}

				offset += ret;
			}

			return true;
		}

		string ReadChunkedBody (Connection connection)
		{
			if (connection.Headers.ContainsKey ("Content-Length"))
				throw new InvalidOperationException ();

			var body = new StringBuilder ();

			do {
				var header = connection.RequestReader.ReadLine ();
				var length = int.Parse (header, NumberStyles.HexNumber);
				if (length == 0)
					break;

				var buffer = new char [length];
				var ret = connection.RequestReader.Read (buffer, 0, length);
				if (ret != length)
					throw new InvalidOperationException ();

				var empty = connection.RequestReader.ReadLine ();
				if (!string.IsNullOrEmpty (empty))
					throw new InvalidOperationException ();

				body.Append (buffer);
			} while (true);

			return body.ToString ();
		}

		public override HttpWebRequest CreateRequest (Uri uri)
		{
			var request = base.CreateRequest (uri);

			if (Body != null)
				request.ContentType = "text/plain";
			request.Method = "POST";

			if (AllowWriteStreamBuffering != null)
				request.AllowWriteStreamBuffering = AllowWriteStreamBuffering.Value;

			if (((Flags & RequestFlags.ExplicitlySetLength) != 0) && (Mode != TransferMode.ContentLength))
				throw new InvalidOperationException ();

			switch (Mode) {
			case TransferMode.Chunked:
				request.SendChunked = true;
				break;
			case TransferMode.ContentLength:
				if (body == null)
					request.ContentLength = 0;
				else
					request.ContentLength = body.Length;
				break;
			}

			if (Body != null) {
				using (var stream = request.GetRequestStream ()) {
					if (!string.IsNullOrEmpty (Body))
						WriteBody (stream, Body);
				}
			}

			return request;
		}

		void WriteBody (Stream writer, string body)
		{
			var buffer = new UTF8Encoding ().GetBytes (body);

			var length = buffer.Length;
			var chunkSize = WriteChunkSize ?? length;
			var minDelay = WriteChunkMinDelay ?? 0;
			var maxDelay = WriteChunkMaxDelay ?? minDelay;

			var random = new Random ();
			var delayRange = maxDelay - minDelay;

			int offset = 0;
			while (offset < length) {
				int delay = minDelay + random.Next (delayRange);
				Thread.Sleep (delay);

				var size = Math.Min (length - offset, chunkSize);
				writer.Write (buffer, offset, size);
				offset += size;
			}
		}
	}
}

