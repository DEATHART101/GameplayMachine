using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace XHelper
{
    public static class SerializationUtils
    {
        #region Byte Serialization
        public interface IByteSerializable
        {
            public int SerializeLength { get; }
            public int SerializeTo(byte[] bytes, int offset);
            public int DeserizlizeFrom(byte[] bytes, int offset);
        }

        public static int TypeSize<T>(T str)
            where T : struct
        {
            var dm = new DynamicMethod("SizeOfType", typeof(int), new Type[] { });
            ILGenerator il = dm.GetILGenerator();
            il.Emit(OpCodes.Sizeof, typeof(T));
            il.Emit(OpCodes.Ret);
            return (int)dm.Invoke(null, null);
        }

        public static int TypeSizeUnSafe(Type type)
        {
            var dm = new DynamicMethod("SizeOfType", typeof(int), new Type[] { });
            ILGenerator il = dm.GetILGenerator();
            il.Emit(OpCodes.Sizeof, type);
            il.Emit(OpCodes.Ret);
            return (int)dm.Invoke(null, null);
        }

        public static int Serialize<T>(byte[] bytes, int offset, int length, T obj)
            where T : IByteSerializable
        {
            if (obj == null)
            {
                return 0;
            }

            int lengthNeeded = obj.SerializeLength;
            if (length < lengthNeeded)
            {
                return 0;
            }

            return obj.SerializeTo(bytes, offset);
        }

        public static int Deserialize<T>(byte[] bytes, int offset, int length, out T result, bool customSize = false)
            where T : IByteSerializable
        {
            result = default;
            int lengthNeeded = result.SerializeLength;
            if (!customSize && length < lengthNeeded)
            {
                //Debug.LogWarning($"iii Deserialization failed: {result.GetType()} length provided: {length} length needed: {lengthNeeded}");
                return 0;
            }

            return result.DeserizlizeFrom(bytes, offset);
        }

        public static int MarshalSerialize<T>(T obj, byte[] bytes, int offset, int length)
            where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            if (length < size)
            {
                return 0;
            }

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(obj, ptr, true);
            Marshal.Copy(ptr, bytes, offset, size);
            Marshal.FreeHGlobal(ptr);

            return size;
        }

        public static int MarshalDeserialize<T>(out T obj, byte[] bytes, int offset, int length)
            where T : struct
        {
            obj = default;
            int size = Marshal.SizeOf(typeof(T));
            if (length < size)
            {
                return 0;
            }

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.Copy(bytes, offset, ptr, size);
            obj = Marshal.PtrToStructure<T>(ptr);
            Marshal.FreeHGlobal(ptr);

            return size;
        }

        public static int MarshalSerializeUnSafe(object obj, Type objType, byte[] bytes, int offset, int length)
        {
            //int size = TypeSizeUnSafe(objType);
            int size = TypeSizeUnSafe(objType);
            if (length < size)
            {
                return 0;
            }

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(obj, ptr, true);
            Marshal.Copy(ptr, bytes, offset, size);
            Marshal.FreeHGlobal(ptr);

            return size;
        }

        public static int MarshalDeserializeUnSafe(out object obj, Type toType, byte[] bytes, int offset, int length)
        {
            obj = Activator.CreateInstance(toType);
            int size = TypeSizeUnSafe(toType);
            if (length < size)
            {
                return 0;
            }

            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.Copy(bytes, offset, ptr, size);
            Marshal.PtrToStructure(ptr, obj);
            Marshal.FreeHGlobal(ptr);

            return size;
        }

        public static int MarshalLength<T>()
            where T : struct
        {
            return Marshal.SizeOf(typeof(T));
        }

        public static int ClassMarshalLength<T>()
            where T : class
        {
            return Marshal.SizeOf(typeof(T));
        }



        #endregion

        #region Convert Serialization

        public interface ISerializeAs<T>
        {
            public void Serialize(T obj);

            public T Deserialize();
        }

        public class SerializeAs : System.Attribute
        {
            public Type AsType { get; set; }

            public SerializeAs(Type asType)
            {
                AsType = asType;
            }
        }

        #endregion
    }
}

