"""Read-only UnityFS inspection using bounded, on-demand block decompression."""
import io, struct, bisect, functools
from pathlib import Path

import hashlib
import UnityPy
from UnityPy.helpers.CompressionHelper import decompress_lzma
import lz4.block

# These scene/asset IDs are a version contract, not portable IDs for other releases.
DONOR_VERSION = "Manimal-Icebreaker-1.1.3"
DONOR_SHA256 = "F3948D1FB4252D2A756F1A5249D7220A500B2510955CEB77E0C89CC8096245C1"

def verify_donor(path):
    """Read a user-supplied local bundle only; never download or modify assets."""
    if not __debug__:
        raise RuntimeError("Run without -O: export assertions are required safety checks")
    path = Path(path).expanduser().resolve(strict=True)
    if not path.is_file():
        raise ValueError("--bundle must be a regular file")
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    if digest.hexdigest().upper() != DONOR_SHA256:
        raise ValueError("Unsupported donor SHA256; only " + DONOR_VERSION + " is supported")
    return path

def safe_output(bundle, output, *, directory):
    """Refuse source aliases and overwrites, including symlinks/hardlinks."""
    source = Path(bundle).resolve(strict=True)
    target = Path(output).expanduser().resolve()
    if source == target or (target.exists() and target.samefile(source)):
        raise ValueError("Output must not overwrite the donor bundle")
    if directory:
        if target in source.parents:
            raise ValueError("Output directory must not contain the donor bundle")
        if target.exists() and (not target.is_dir() or any(target.iterdir())):
            raise ValueError("--output must be a new or empty directory")
    elif target.exists():
        raise ValueError("--output must be a new file; refusing to overwrite")
    return target

def unpack(f, fmt):
    return struct.unpack('>' + fmt, f.read(struct.calcsize('>' + fmt)))

def cstr(f):
    b = bytearray()
    while True:
        c = f.read(1)
        if not c:
            raise EOFError('unterminated string')
        if c == b'\0':
            return b.decode('utf8')
        b.extend(c)

def decompress(b, size, flag):
    flag &= 63
    if flag == 0:
        result = b
    elif flag in (2, 3):
        result = lz4.block.decompress(b, uncompressed_size=size)
    elif flag == 1:
        result = decompress_lzma(b)
    else:
        raise ValueError(f'unsupported compression {flag}')
    assert len(result) == size
    return result

class Bundle:
    def __init__(self, path):
        self.f = path.open('rb')
        f = self.f
        signature = cstr(f)
        assert signature == 'UnityFS'
        version, = unpack(f, 'I')
        player, engine = cstr(f), cstr(f)
        total, comp, raw, flags = unpack(f, 'QIII')
        if version >= 7:
            f.seek((f.tell() + 15) & ~15)
        start = f.tell()
        if flags & 128:
            f.seek(total - comp)
        info = io.BytesIO(decompress(f.read(comp), raw, flags))
        if flags & 128:
            f.seek(start)
        if flags & 512:
            f.seek((f.tell() + 15) & ~15)
        data_start = f.tell()
        info.read(16)
        count, = unpack(info, 'I')
        self.blocks, self.offsets = [], []
        uncompressed, compressed = 0, data_start
        for _ in range(count):
            u, c, fl = unpack(info, 'IIH')
            self.blocks.append((compressed, c, u, fl))
            self.offsets.append(uncompressed)
            compressed += c
            uncompressed += u
        n, = unpack(info, 'I')
        self.nodes = []
        for _ in range(n):
            offset, size, fl = unpack(info, 'QQI')
            self.nodes.append(dict(offset=offset, size=size, flags=fl, path=cstr(info)))
        self.header = dict(signature=signature, version=version, player=player,
                           engine=engine, bytes=total, flags=flags,
                           blocks=count, decompressed_bytes=uncompressed,
                           nodes=self.nodes)

    @functools.lru_cache(maxsize=128)
    def block(self, i):
        pos, c, u, fl = self.blocks[i]
        self.f.seek(pos)
        return decompress(self.f.read(c), u, fl)

    def read(self, offset, size):
        parts = []
        while size > 0:
            i = bisect.bisect_right(self.offsets, offset) - 1
            data = self.block(i)
            local = offset - self.offsets[i]
            n = min(size, len(data) - local)
            if n <= 0:
                raise EOFError(offset)
            parts.append(data[local:local+n])
            offset += n
            size -= n
        return b''.join(parts)

class Slice(io.RawIOBase):
    def __init__(self, bundle, node):
        self.bundle, self.node, self.pos = bundle, node, 0
        self.name = node['path']
    def tell(self):
        return self.pos
    def seek(self, offset, whence=0):
        self.pos = offset if whence == 0 else self.pos + offset if whence == 1 else self.node['size'] + offset
        if self.pos < 0:
            raise ValueError('negative position')
        return self.pos
    def read(self, size=-1):
        size = self.node['size'] - self.pos if size < 0 else min(size, self.node['size'] - self.pos)
        data = self.bundle.read(self.node['offset'] + self.pos, max(0, size))
        self.pos += len(data)
        return data
    def readable(self): return True
    def seekable(self): return True
