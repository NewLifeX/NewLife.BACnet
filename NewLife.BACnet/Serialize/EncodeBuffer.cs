namespace System.IO.BACnet.Serialize;

/// <summary>可扩展的 APDU 编码缓冲区，支持分片滑动窗口写入。用于 BACnet 报文序列化过程中的临时数据存储和偏移管理。</summary>
/// <remarks>
/// 缓冲区支持两种模式：
/// - 可扩展模式（默认）：自动扩容，适用于编码场景
/// - 固定大小模式：不扩容，超出时报 NotEnoughBuffer，适用于解码和分片场景
/// serialize_counter 和 min_limit 配合实现分片滑动窗口：仅当 serialize_counter >= min_limit 时实际写入数据。
/// </remarks>
public class EncodeBuffer
{
    /// <summary>编码数据缓冲区</summary>
    public byte[] buffer;
    /// <summary>当前写入偏移。会超过 max_offset（用于计算所需空间）</summary>
    public int offset;
    /// <summary>最大允许偏移，超过此值不再写入</summary>
    public int max_offset;
    /// <summary>序列化计数器，配合 min_limit 实现分片滑动窗口</summary>
    public int serialize_counter;
    /// <summary>最小写入限制，serialize_counter 低于此值时跳过实际写入</summary>
    public int min_limit;
    /// <summary>编码结果状态</summary>
    public EncodeResult result;
    /// <summary>是否可自动扩容</summary>
    public bool expandable;

    /// <summary>创建可扩展的编码缓冲区（初始容量 128 字节）</summary>
    public EncodeBuffer()
    {
        expandable = true;
        buffer = new byte[128];
        max_offset = buffer.Length - 1;
    }

    /// <summary>创建固定大小的编码缓冲区</summary>
    /// <param name="buffer">预分配缓冲区。为 null 时使用空数组。</param>
    /// <param name="offset">起始写入偏移</param>
    public EncodeBuffer(byte[] buffer, int offset)
    {
        if (buffer == null) buffer = new byte[0];
        expandable = false;
        this.buffer = buffer;
        this.offset = offset;
        max_offset = buffer.Length;
    }

    /// <summary>递增偏移和序列化计数器。配合滑动窗口分片：仅当 serialize_counter >= min_limit 时实际移动 offset。</summary>
    public void Increment()
    {
        if (offset < max_offset)
        {
            if (serialize_counter >= min_limit)
                offset++;
            serialize_counter++;
        }
        else
        {
            if (serialize_counter >= min_limit)
                offset++;
        }
    }

    /// <summary>写入单个字节。可扩展模式下缓冲区满时自动扩容。</summary>
    /// <param name="b">要写入的字节</param>
    public void Add(byte b)
    {
        if (offset < max_offset)
        {
            if (serialize_counter >= min_limit)
                buffer[offset] = b;
        }
        else
        {
            if (expandable)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
                max_offset = buffer.Length - 1;
                if (serialize_counter >= min_limit)
                    buffer[offset] = b;
            }
            else
                result |= EncodeResult.NotEnoughBuffer;
        }

        Increment();
    }

    /// <summary>批量写入多个字节</summary>
    /// <param name="buffer">源字节数组</param>
    /// <param name="count">要写入的字节数</param>
    public void Add(byte[] buffer, int count)
    {
        for (var i = 0; i < count; i++)
            Add(buffer[i]);
    }

    /// <summary>计算与另一个缓冲区的偏移差值（用于分片进度跟踪）</summary>
    /// <param name="buffer">对比的编码缓冲区</param>
    /// <returns>offset 和 serialize_counter 的差值中的较大值</returns>
    public int GetDiff(EncodeBuffer buffer)
    {
        var diff = Math.Abs(buffer.offset - offset);
        diff = Math.Max(Math.Abs(buffer.serialize_counter - serialize_counter), diff);
        return diff;
    }

    /// <summary>创建当前缓冲区的浅拷贝副本</summary>
    /// <returns>新的 EncodeBuffer 实例，与原实例共享 buffer 引用</returns>
    public EncodeBuffer Copy()
    {
        return new EncodeBuffer
        {
            buffer = buffer,
            max_offset = max_offset,
            min_limit = min_limit,
            offset = offset,
            result = result,
            serialize_counter = serialize_counter,
            expandable = expandable
        };
    }

    /// <summary>提取已写入的有效数据为新的字节数组</summary>
    /// <returns>长度为 offset 的字节数组</returns>
    public byte[] ToArray()
    {
        var ret = new byte[offset];
        Array.Copy(buffer, 0, ret, 0, ret.Length);
        return ret;
    }

    /// <summary>重置缓冲区到指定偏移位置，清空序列化计数器和错误状态</summary>
    /// <param name="newOffset">新的起始偏移</param>
    public void Reset(int newOffset)
    {
        offset = newOffset;
        serialize_counter = 0;
        result = EncodeResult.Good;
    }

    /// <summary>返回当前偏移和序列化计数器的字符串表示</summary>
    public override string ToString()
    {
        return offset + " - " + serialize_counter;
    }

    /// <summary>获取实际写入长度（offset 和 max_offset 的较小值）</summary>
    public int GetLength()
    {
        return Math.Min(offset, max_offset);
    }
}
