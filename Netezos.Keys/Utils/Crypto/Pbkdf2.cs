﻿using System;
using System.IO;

#if !WINDOWS_UWP && !USEBC
using System.Security.Cryptography;
#endif

namespace Netezos.Keys.Utils.Crypto
{
	public class Pbkdf2 : Stream
	{
		#region PBKDF2
		byte[] _saltBuffer, _digest, _digestT1;

		KeyedHashAlgorithm _hmacAlgorithm;

		int _iterations;

		public Pbkdf2(KeyedHashAlgorithm hmacAlgorithm, byte[] salt, int iterations)
		{

			int hmacLength = hmacAlgorithm.HashSize / 8;
			_saltBuffer = new byte[salt.Length + 4];
			Array.Copy(salt, _saltBuffer, salt.Length);
			_iterations = iterations;
			_hmacAlgorithm = hmacAlgorithm;
			_digest = new byte[hmacLength];
			_digestT1 = new byte[hmacLength];
		}
		
		/// <summary>
		/// Reads from the derived key stream.
		/// </summary>
		/// <param name="count">The number of bytes to read.</param>
		/// <returns>Bytes from the derived key stream.</returns>
		public byte[] Read(int count)
		{
			byte[] buffer = new byte[count];
			int num = Read(buffer, 0, count);
			if (num < count)
				throw new ArgumentException("Can only return {0} bytes.");
			return buffer;
		}

		/// <summary>
		/// Computes a derived key.
		/// </summary>
		/// <param name="hmacAlgorithm">
		/// </param>
		/// <param name="salt">
		///     The salt.
		///     A unique salt means a unique derived key, even if the original key is identical.
		/// </param>
		/// <param name="iterations">The number of iterations to apply.</param>
		/// <param name="derivedKeyLength">The desired length of the derived key.</param>
		/// <returns>The derived key.</returns>
		public static byte[] ComputeDerivedKey(KeyedHashAlgorithm hmacAlgorithm, byte[] salt, int iterations,
											   int derivedKeyLength)
		{

			using (Pbkdf2 kdf = new Pbkdf2(hmacAlgorithm, salt, iterations))
			{
				return kdf.Read(derivedKeyLength);
			}
		}
		public override void Close()
		{
			DisposeHmac();
		}

		private void DisposeHmac()
		{
			_hmacAlgorithm.Clear();
		}
		
		void BEBytesFromUInt32(uint value, byte[] bytes, int offset)
		{
			bytes[offset + 0] = (byte)(value >> 24);
			bytes[offset + 1] = (byte)(value >> 16);
			bytes[offset + 2] = (byte)(value >> 8);
			bytes[offset + 3] = (byte)(value);
		}

		void ComputeBlock(uint pos)
		{
			BEBytesFromUInt32(pos, _saltBuffer, _saltBuffer.Length - 4);
			ComputeHmac(_saltBuffer, _digestT1);
			Array.Copy(_digestT1, _digest, _digestT1.Length);

			for (int i = 1; i < _iterations; i++)
			{
				ComputeHmac(_digestT1, _digestT1);
				for (int j = 0; j < _digest.Length; j++)
				{
					_digest[j] ^= _digestT1[j];
				}
			}
		}

		void ComputeHmac(byte[] input, byte[] output)
		{
			_hmacAlgorithm.Initialize();
			_hmacAlgorithm.TransformBlock(input, 0, input.Length, input, 0);
			_hmacAlgorithm.TransformFinalBlock(new byte[0], 0, 0);
			Array.Copy(_hmacAlgorithm.Hash, output, output.Length);
		}
		#endregion

		#region Stream
		long _blockStart, _blockEnd, _pos;

		/// <exclude />
		public override void Flush()
		{

		}

		/// <inheritdoc />
		public override int Read(byte[] buffer, int offset, int count)
		{
			int bytes = 0;

			while (count > 0)
			{
				if (Position < _blockStart || Position >= _blockEnd)
				{
					if (Position >= Length)
					{
						break;
					}

					long pos = Position / _digest.Length;
					ComputeBlock((uint)(pos + 1));
					_blockStart = pos * _digest.Length;
					_blockEnd = _blockStart + _digest.Length;
				}

				int bytesSoFar = (int)(Position - _blockStart);
				int bytesThisTime = (int)Math.Min(_digest.Length - bytesSoFar, count);
				Array.Copy(_digest, bytesSoFar, buffer, bytes, bytesThisTime);
				count -= bytesThisTime;
				bytes += bytesThisTime;
				Position += bytesThisTime;
			}

			return bytes;
		}

		/// <inheritdoc />
		public override long Seek(long offset, SeekOrigin origin)
		{
			long pos;

			switch (origin)
			{
				case SeekOrigin.Begin:
					pos = offset;
					break;
				case SeekOrigin.Current:
					pos = Position + offset;
					break;
				case SeekOrigin.End:
					pos = Length + offset;
					break;
				default:
					throw new ArgumentException();
			}

			Position = pos;
			return pos;
		}
		
		public override void SetLength(long value) => throw new NotImplementedException();

		/// <exclude />
		public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();


		/// <exclude />
		public override bool CanRead => true;

		/// <exclude />
		public override bool CanSeek => true;

		/// <exclude />
		public override bool CanWrite => false;

		/// <summary>
		/// The maximum number of bytes that can be derived is 2^32-1 times the HMAC size.
		/// </summary>
		public override long Length => _digest.Length * uint.MaxValue;

		/// <summary>
		/// The position within the derived key stream.
		/// </summary>
		public override long Position
		{
			get => _pos;
			set => _pos = value;
		}
		#endregion
	}
}
