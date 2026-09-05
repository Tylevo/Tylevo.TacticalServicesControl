using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Fika.Core.Networking.LiteNetLib.Utils;

public interface INetSerializable
{
	void Serialize(NetDataWriter writer);
	void Deserialize(NetDataReader reader);
}

public sealed class NetDataWriter
{
	private readonly MemoryStream _stream = new();
	private readonly BinaryWriter _writer;

	public NetDataWriter()
	{
		_writer = new BinaryWriter(_stream, Encoding.UTF8, leaveOpen: true);
	}

	public int Length => checked((int)_stream.Length);
	public byte[] Data => CopyData();

	public void Put(bool value) => _writer.Write(value);
	public void Put(int value) => _writer.Write(value);
	public void Put(float value) => _writer.Write(value);
	public void Put(string value) => _writer.Write(value ?? string.Empty);
	public void PutUnmanaged<T>(T value) where T : unmanaged
	{
		byte[] bytes = new byte[Marshal.SizeOf<T>()];
		MemoryMarshal.Write(bytes.AsSpan(), in value);
		_writer.Write(bytes);
	}

	public byte[] CopyData()
	{
		_writer.Flush();
		return _stream.ToArray();
	}

	public byte[] ToArray() => CopyData();
}

public sealed class NetDataReader
{
	private MemoryStream _stream = new(Array.Empty<byte>(), writable: false);
	private BinaryReader _reader;

	public NetDataReader()
	{
		_reader = CreateReader(_stream);
	}

	public NetDataReader(byte[] data)
	{
		_stream.Dispose();
		_stream = new MemoryStream(data ?? Array.Empty<byte>(), writable: false);
		_reader = CreateReader(_stream);
	}

	public int AvailableBytes => checked((int)(_stream.Length - _stream.Position));

	public void SetSource(byte[] data)
	{
		_reader.Dispose();
		_stream.Dispose();
		_stream = new MemoryStream(data ?? Array.Empty<byte>(), writable: false);
		_reader = CreateReader(_stream);
	}

	public bool GetBool() => _reader.ReadBoolean();
	public int GetInt() => _reader.ReadInt32();
	public float GetFloat() => _reader.ReadSingle();
	public string GetString() => _reader.ReadString();
	public T GetUnmanaged<T>() where T : unmanaged
	{
		int size = Marshal.SizeOf<T>();
		byte[] bytes = _reader.ReadBytes(size);
		if (bytes.Length != size)
		{
			throw new EndOfStreamException();
		}

		return MemoryMarshal.Read<T>(bytes);
	}

	private static BinaryReader CreateReader(Stream stream)
	{
		return new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
	}
}
