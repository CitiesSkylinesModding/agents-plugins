// The zip format's record signatures: PK\x03\x04, PK\x01\x02, and PK\x05\x06, little-endian.
const LOCAL_HEADER = 67_324_752;
const DIRECTORY_HEADER = 33_639_248;
const END_OF_DIRECTORY = 101_010_256;

/**
 * A zip archive storing its entries uncompressed, as the game's `.cok` archives do.
 */
export function storedZip(entries: Readonly<Record<string, string>>): Buffer {
  const locals: Buffer[] = [];
  const directory: Buffer[] = [];

  let offset = 0;

  for (const [name, content] of Object.entries(entries)) {
    const nameBytes = Buffer.from(name);
    const data = Buffer.from(content);
    const local = Buffer.alloc(30);
    const header = Buffer.alloc(46);

    local.writeUInt32LE(LOCAL_HEADER, 0);
    local.writeUInt32LE(data.length, 18);
    local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(nameBytes.length, 26);

    header.writeUInt32LE(DIRECTORY_HEADER, 0);
    header.writeUInt32LE(data.length, 20);
    header.writeUInt32LE(data.length, 24);
    header.writeUInt16LE(nameBytes.length, 28);
    header.writeUInt32LE(offset, 42);

    locals.push(local, nameBytes, data);
    directory.push(header, nameBytes);
    offset += local.length + nameBytes.length + data.length;
  }

  const directoryBytes = Buffer.concat(directory);
  const end = Buffer.alloc(22);

  end.writeUInt32LE(END_OF_DIRECTORY, 0);
  end.writeUInt32LE(directoryBytes.length, 12);
  end.writeUInt32LE(offset, 16);

  return Buffer.concat([...locals, directoryBytes, end]);
}
