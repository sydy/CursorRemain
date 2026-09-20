import Foundation

#if canImport(Compression)
import Compression

enum GzipCodec {
    static func compress(_ data: Data) throws -> Data {
        let deflated = try deflate(data)
        var out = Data([0x1f, 0x8b, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xff])
        out.append(deflated)
        var crc = crc32(data).littleEndian
        var isize = UInt32(truncatingIfNeeded: data.count).littleEndian
        out.append(contentsOf: withUnsafeBytes(of: &crc, Array.init))
        out.append(contentsOf: withUnsafeBytes(of: &isize, Array.init))
        return out
    }

    static func decompress(_ data: Data) throws -> Data {
        let data = Data(data)
        guard data.count >= 18, data[0] == 0x1f, data[1] == 0x8b, data[2] == 0x08 else {
            throw CursorAPIError("同步文件损坏")
        }
        var offset = 10
        let flags = data[3]
        if flags & 0x04 != 0 {
            guard data.count >= offset + 2 else { throw CursorAPIError("同步文件损坏") }
            let extra = Int(data[offset]) | (Int(data[offset + 1]) << 8)
            offset += 2 + extra
        }
        if flags & 0x08 != 0 {
            while offset < data.count, data[offset] != 0 { offset += 1 }
            offset += 1
        }
        if flags & 0x10 != 0 {
            while offset < data.count, data[offset] != 0 { offset += 1 }
            offset += 1
        }
        if flags & 0x02 != 0 { offset += 2 }
        guard data.count >= offset + 8 else { throw CursorAPIError("同步文件损坏") }
        let tail = [UInt8](data.suffix(4))
        let isize = Int(UInt32(tail[0]) | UInt32(tail[1]) << 8 | UInt32(tail[2]) << 16 | UInt32(tail[3]) << 24)
        let payload = data.subdata(in: offset..<(data.count - 8))
        return try inflate(payload, destHint: isize)
    }

    static func deflate(_ data: Data) throws -> Data {
        try transcode(data, encode: true)
    }

    static func inflate(_ data: Data, destHint: Int = 0) throws -> Data {
        try transcode(data, encode: false, destHint: destHint)
    }

    static func transcode(_ data: Data, encode: Bool, destHint: Int = 0) throws -> Data {
        var destCap = encode
            ? max(64, data.count + 64 + data.count / 4)
            : max(4096, max(destHint, data.count * 16))
        for _ in 0..<8 {
            var dest = Data(count: destCap)
            let written: Int = dest.withUnsafeMutableBytes { destPtr in
                data.withUnsafeBytes { srcPtr in
                    guard let src = srcPtr.bindMemory(to: UInt8.self).baseAddress,
                          let dst = destPtr.bindMemory(to: UInt8.self).baseAddress
                    else { return 0 }
                    return encode
                        ? compression_encode_buffer(dst, destCap, src, data.count, nil, COMPRESSION_ZLIB)
                        : compression_decode_buffer(dst, destCap, src, data.count, nil, COMPRESSION_ZLIB)
                }
            }
            if written > 0 {
                let truncated = written == destCap && (destHint <= 0 || written < destHint)
                if truncated {
                    destCap *= 2
                    continue
                }
                dest.count = written
                return dest
            }
            destCap *= 2
        }
        throw CursorAPIError("同步文件损坏")
    }

    static func crc32(_ data: Data) -> UInt32 {
        var crc: UInt32 = 0xffff_ffff
        for byte in data {
            crc ^= UInt32(byte)
            for _ in 0..<8 {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb8_8320 : crc >> 1
            }
        }
        return crc ^ 0xffff_ffff
    }
}

#else
enum GzipCodec {
    static func compress(_ data: Data) throws -> Data {
        throw CursorAPIError("当前平台无法压缩同步文件")
    }

    static func decompress(_ data: Data) throws -> Data {
        throw CursorAPIError("当前平台无法解压同步文件")
    }
}
#endif
