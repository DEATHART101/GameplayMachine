using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XStreams
{
    public class BitStream
    {
        #region Statics

        private static Encoding s_stringEncoder;

        static BitStream()
        {
            s_stringEncoder = Encoding.UTF8;
        }

        public static Encoding StringEncoder
        {
            get
            {
                return s_stringEncoder;
            }

            set
            {
                s_stringEncoder = value;
            }
        }

        #endregion

        // Constantly points at the start of next read, for example
        // 125 85 46    0    0 0
        //              ↑
        private int m_bytePointer;
        private int m_bitPointer;

        private int m_streamLength;
        private byte[] m_target;

        #region Interfaces

        public byte[] Target
        {
            get
            {
                return m_target;
            }

            set
            {
                m_target = value;
                BytePointer = 0;
            }
        }

        public int LeftLength
        {
            get
            {
                return StreamLength - BytePointer;
            }
        }

        public int StreamLength
        {
            get
            {
                return m_streamLength < 0 ? m_target.Length : m_streamLength;
            }

            set
            {
                if (value > m_target.Length)
                {
                    throw new Exception("Stream length is invalid.");
                }

                m_streamLength = value;
            }
        }

        // Only GetBit & WriteBit are affected by bit pointer
        public int BitPointer
        {
            get
            {
                return m_bitPointer;
            }

            set
            {
                if (value >= 0 && value <= 7)
                {
                    m_bitPointer = value;
                }
                else
                {
                    int total_bit_pointer = m_bytePointer * 8 + value;

                    BytePointer = total_bit_pointer / 8;
                    m_bitPointer = total_bit_pointer % 8;
                }
            }
        }

        public int BytePointer
        {
            get
            {
                return m_bytePointer;
            }

            set
            {
                m_bytePointer = value < 0 ? 0 : value;
                m_bitPointer = 0;
            }
        }

        public BitStream()
        {
            m_streamLength = -1;
            m_bytePointer = 0;
            m_target = null;
        }

        public BitStream(byte[] target)
        {
            m_streamLength = -1;
            m_bytePointer = 0;
            m_target = target;
        }

        public void CarrigeReturn()
        {
            BytePointer = 0;
        }

        public void GetVoid(int byte_length)
        {
            BytePointer += byte_length;
        }

        public int GetBit()
        {
            CheckAtEnd();

            int result = (m_target[m_bytePointer] >> m_bitPointer) & 1;
            BitPointer = m_bitPointer + 1;

            return result;
        }

        public void WriteBit(int one_or_zero)
        {
            CheckAtEnd();

            one_or_zero = one_or_zero & 1;
            m_target[m_bytePointer] = (byte)(m_target[m_bytePointer] | (one_or_zero << m_bitPointer));
            BitPointer = m_bitPointer + 1;
        }

        // bytes: 0...1...2.....
        // int: high.....low
        public int GetInt(int byte_length = 4)
        {
            CheckAtEnd(byte_length);

            int result = GetIntWithBuffer(m_target, m_bytePointer, byte_length);

            BytePointer += byte_length;

            return result;
        }

        public void WriteInt(int value, int byte_length = 4)
        {
            CheckAtEnd(byte_length);

            WriteIntToBuffer(m_target, m_bytePointer, value, byte_length);

            BytePointer += byte_length;
        }

        public int GetSmartInt()
        {
            CheckAtEnd();

            int value;
            int bytes_read = GetSmartIntWithBuffer(m_target, m_bytePointer, out value);

            CheckAtEnd(bytes_read);

            BytePointer = m_bytePointer + bytes_read;

            return value;
        }

        public void WriteSmartInt(int value)
        {
            CheckAtEnd();

            int bytes_write = WriteSmartIntToBuffer(m_target, m_bytePointer, value);

            CheckAtEnd(bytes_write);

            BytePointer = m_bytePointer + bytes_write;
        }

        public float GetFloat()
        {
            CheckAtEnd(sizeof(float));

            float result = GetFloatWithBuffer(m_target, m_bytePointer);

            BytePointer += sizeof(float);

            return result;
        }

        public void WriteFloat(float value)
        {
            CheckAtEnd(sizeof(float));

            WriteFloatToBuffer(m_target, m_bytePointer, value);

            BytePointer += sizeof(float);
        }

        public void GetBytes(byte[] dest, int offset, int get_count)
        {
            if (get_count <= 0)
            {
                return;
            }

            CheckAtEnd(get_count);

            CopyBuffer(m_target, dest, get_count, m_bytePointer, offset);

            BytePointer = m_bytePointer + get_count;
        }

        public byte[] GetBytes(int get_count)
        {
            if (get_count <= 0)
            {
                return null;
            }

            CheckAtEnd(get_count);

            byte[] result = new byte[get_count];
            CopyBuffer(m_target, result, get_count, m_bytePointer, 0);

            BytePointer = m_bytePointer + get_count;

            return result;
        }

        public void WriteBytes(IList<byte> bytes)
        {
            int count = bytes.Count;

            CheckAtEnd(count);

            for (int i = 0; i < count; i++)
            {
                m_target[m_bytePointer + i] = bytes[i];
            }

            BytePointer = m_bytePointer + count;
        }

        public void WriteBytes(byte[] value, int offset = 0, int count = -1)
        {
            count = count < 0 ? value.Length - offset : count;

            CheckAtEnd(count);

            CopyBuffer(value, m_target, count, offset, m_bytePointer);

            BytePointer = m_bytePointer + count;
        }

        public string GetString(int byte_length)
        {
            CheckAtEnd(byte_length);

            string result = GetStringWithBuffer(m_target, m_bytePointer, byte_length);

            BytePointer = m_bytePointer + byte_length;
            return result;
        }

        public string GetStringWithHeader(int header_length)
        {
            CheckAtEnd();

            int header = GetIntWithBuffer(m_target, m_bytePointer, header_length);

            CheckAtEnd(header_length + header);

            string result = GetStringWithBuffer(m_target, m_bytePointer + header_length, header);

            BytePointer = m_bytePointer + header_length + header;
            return result;
        }

        public int WriteString(string str)
        {
            CheckAtEnd();

            int write_bytes = WriteStringToBuffer(m_target, m_bytePointer, str);

            CheckAtEnd(write_bytes);

            BytePointer = m_bytePointer + write_bytes;
            return write_bytes;
        }

        public void WriteStringWithHeader(string str, int header_length)
        {
            CheckAtEnd();

            int write_bytes = WriteStringToBuffer(m_target, m_bytePointer + header_length, str);

            CheckAtEnd(header_length + write_bytes);

            WriteIntToBuffer(m_target, m_bytePointer, write_bytes, header_length);
            BytePointer = m_bytePointer + header_length + write_bytes;
        }

        #region Statics

        public static void WriteIntToBuffer(byte[] buffer, int offset, int value, int byte_length = 4)
        {
            if (byte_length > 4 || byte_length < 0)
            {
                throw new Exception("Length is not allowed.");
            }

            for (int i = 0; i < byte_length; i++)
            {
                int mask_offset = 8 * (byte_length - i - 1);
                int mask = 255 << mask_offset;
                buffer[offset + i] = (byte)((value & mask) >> mask_offset);
            }
        }

        public static int GetIntWithBuffer(byte[] buffer, int offset, int byte_length = 4)
        {
            if (byte_length > 4 || byte_length < 0)
            {
                throw new Exception("Length is not allowed.");
            }

            int result = 0;
            for (int i = 0; i < byte_length; i++)
            {
                result = result | (((int)buffer[offset + i]) << (8 * (byte_length - i - 1)));
            }

            return result;
        }

        // Returns how many bytes are written
        public static int WriteSmartIntToBuffer(byte[] buffer, int offset, int value)
        {
            int bit_used = 32 - LeadingZeros(value);
            int byte_use = 0;
            if (bit_used <= 7)
            {
                byte_use = 1;
            }
            else if (bit_used <= 14)
            {
                byte_use = 2;
            }
            else if (bit_used <= 21)
            {
                byte_use = 3;
            }
            else
            {
                byte_use = 4;
            }

            for (int i = 0; i < byte_use - 1; i++)
            {
                value = value | (1 << ((byte_use * 8) - i - 1));
            }

            WriteIntToBuffer(buffer, offset, value, byte_use);

            return byte_use;
        }

        // Returns how many bytes are read
        public static int GetSmartIntWithBuffer(byte[] buffer, int offset, out int value)
        {
            int header_byte = buffer[offset];
            int byte_used = 0;
            while (true)
            {
                if (((1 << (8 - byte_used)) & header_byte) != 0)
                {
                    byte_used++;
                }
                else
                {
                    break;
                }
            }

            int header_mask = ~((1 << 31) >> byte_used);
            value = GetIntWithBuffer(buffer, offset, byte_used) & header_mask;

            return byte_used;
        }

        public static void WriteFloatToBuffer(byte[] buffer, int offset, float value)
        {
            XHelper.SerializationUtils.MarshalSerialize<float>(value, buffer, offset, buffer.Length - offset);
        }

        public static float GetFloatWithBuffer(byte[] buffer, int offset)
        {
            float result;
            XHelper.SerializationUtils.MarshalDeserialize<float>(out result, buffer, offset, buffer.Length - offset);
            return result;
        }

        public int WriteStringToBuffer(byte[] buffer, int offset, string str)
        {
            return s_stringEncoder.GetBytes(str, 0, str.Length, buffer, offset);
        }

        public string GetStringWithBuffer(byte[] buffer, int offset, int length)
        {
            return s_stringEncoder.GetString(buffer, offset, length);
        }

        public static void ClearBuffer(byte[] buffer, int clear_length = -1)
        {
            int length = clear_length == -1 ? buffer.Length : clear_length;
            for (int i = 0; i < length; i++)
            {
                buffer[i] = 0;
            }
        }

        public static void CopyBuffer(byte[] source, byte[] dest, int count, int src_offset = 0, int dest_offset = 0)
        {
            for (int i = 0; i < count; i++)
            {
                dest[i + dest_offset] = source[i + src_offset];
            }
        }

        #endregion

        public static int LeadingZeros(int x)
        {
            const int numIntBits = sizeof(int) * 8; //compile time constant
                                                    //do the smearing
            x |= x >> 1;
            x |= x >> 2;
            x |= x >> 4;
            x |= x >> 8;
            x |= x >> 16;
            //count the ones
            x -= x >> 1 & 0x55555555;
            x = (x >> 2 & 0x33333333) + (x & 0x33333333);
            x = (x >> 4) + x & 0x0f0f0f0f;
            x += x >> 8;
            x += x >> 16;
            return numIntBits - (x & 0x0000003f); //subtract # of 1s from 32
        }

        #endregion

        private void CheckAtEnd(int bytes_to_write = 0)
        {
            int stream_length = StreamLength;
            if (m_bytePointer >= stream_length)
            {
                throw new Exception("Stream is at end.");
            }

            if (m_bytePointer + bytes_to_write > stream_length)
            {
                throw new Exception("Stream is not big enough.");
            }
        }
    }
}
