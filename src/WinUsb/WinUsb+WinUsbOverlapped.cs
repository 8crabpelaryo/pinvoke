﻿// Copyright © .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace PInvoke
{
    using System;
    using System.Buffers;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;

    /// <content>
    /// Contains the nested <see cref="WinUsbOverlapped"/> type.
    /// </content>
    public static partial class WinUsb
    {
        /// <summary>
        /// A managed implementation of the <see cref="Kernel32.OVERLAPPED"/> structure, used to support overlapped WinUSB I/O.
        /// </summary>
        private class WinUsbOverlapped : Overlapped
        {
            private readonly Memory<byte> buffer;
            private readonly SafeUsbHandle handle;
            private readonly byte pipeID;

            /// <summary>
            /// The source for completing the <see cref="Completion"/> property.
            /// </summary>
            private readonly TaskCompletionSource<int> completion = new TaskCompletionSource<int>();

            private unsafe NativeOverlapped* native;

            /// <summary>
            /// Initializes a new instance of the <see cref="WinUsbOverlapped"/> class.
            /// </summary>
            /// <param name="handle">
            /// A handle to the WinUSB device on which the I/O is being performed.
            /// </param>
            /// <param name="pipeID">
            /// The ID of the pipe on which the I/O is being performed.
            /// </param>
            /// <param name="buffer">
            /// The buffer which is used by the I/O operation. This buffer will be pinned for the duration of
            /// the operation.
            /// </param>
            public WinUsbOverlapped(SafeUsbHandle handle, byte pipeID, Memory<byte> buffer)
            {
                this.handle = handle ?? throw new ArgumentNullException(nameof(handle));
                this.pipeID = pipeID;
                this.buffer = buffer;
            }

            /// <summary>
            /// Gets a <see cref="MemoryHandle"/> to the transfer buffer.
            /// </summary>
            public MemoryHandle BufferHandle { get; private set; }

            /// <summary>
            /// Gets the amount of bytes transferred.
            /// </summary>
            public uint BytesTransferred { get; private set; }

            /// <summary>
            /// Gets the error code returned by the device driver.
            /// </summary>
            public uint ErrorCode { get; private set; }

            /// <summary>
            /// Gets a task whose result is the number of bytes transferred, or faults with the <see cref="Win32Exception"/> describing the failure.
            /// </summary>
            public Task<int> Completion => this.completion.Task;

            /// <summary>
            /// Packs the current <see cref="WinUsbOverlapped"/> into a <see cref="NativeOverlapped"/> structure.
            /// </summary>
            /// <returns>
            /// An unmanaged pointer to a <see cref="NativeOverlapped"/> structure.
            /// </returns>
            public unsafe NativeOverlapped* Pack()
            {
                this.BufferHandle = this.buffer.Pin();

                this.native = this.Pack(
                    this.DeviceIOControlCompletionCallback,
                    null);

                return this.native;
            }

            /// <summary>
            /// Unpacks the unmanaged <see cref="NativeOverlapped"/> structure into
            /// a managed <see cref="WinUsbOverlapped"/> object.
            /// </summary>
            public unsafe void Unpack()
            {
                Overlapped.Unpack(this.native);
                Overlapped.Free(this.native);
                this.native = null;

                this.BufferHandle.Dispose();
            }

            /// <summary>
            /// Cancels the asynchronous I/O operation.
            /// </summary>
            public unsafe void Cancel()
            {
                if (!WinUsb_AbortPipe(
                    this.handle,
                    this.pipeID))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }

            private unsafe void DeviceIOControlCompletionCallback(uint errorCode, uint numberOfBytesTransferred, NativeOverlapped* nativeOverlapped)
            {
                this.Unpack();

                this.BytesTransferred = numberOfBytesTransferred;
                this.ErrorCode = errorCode;

                if (this.ErrorCode != 0)
                {
                    this.completion.SetException(
                        new Win32Exception((int)this.ErrorCode));
                }
                else
                {
                    this.completion.SetResult((int)numberOfBytesTransferred);
                }
            }
        }
    }
}
