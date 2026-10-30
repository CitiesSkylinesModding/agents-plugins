// Reads the guid of a user asset from its `.cok` archive, the zip the game writes it in, which
// holds the guid as a `<name>.<type>.cid` entry.

import { type FileHandle, access, open, readdir } from 'node:fs/promises';
import path from 'node:path';

export type AssetFolder = 'Saves' | 'Maps';

/**
 * Reads the guid of the asset by its name in the game's list.
 * A save's guid changes each time the game writes it, so each call reads it.
 * Reads only the archive's directory and that entry: a save weighs tens of megabytes.
 */
export async function readAssetGuid(
  userDataPath: string,
  folder: AssetFolder,
  name: string,
  type: string
): Promise<string> {
  const archivePath = await findArchive(path.join(userDataPath, folder), name);
  const archive = await open(archivePath);

  try {
    const entry = await readStoredZipEntry(archive, archivePath, `${name}.${type}.cid`);
    const guid = new TextDecoder().decode(entry).trim();

    if (!isAssetGuid(guid)) {
      throw new Error(`${archivePath} holds no guid in its ${name}.${type}.cid.`);
    }

    return guid;
  } finally {
    await archive.close();
  }
}

export function isAssetGuid(text: string): boolean {
  return /^[\da-f]{32}$/u.test(text);
}

/**
 * Finds the asset's archive: the assets lie in a folder per Steam user.
 */
async function findArchive(root: string, name: string): Promise<string> {
  const users = await readdir(root).catch((): string[] => []);

  for (const user of users) {
    const candidate = path.join(root, user, `${name}.cok`);
    const isFound = await access(candidate).then(
      () => true,
      () => false
    );

    if (isFound) {
      return candidate;
    }
  }

  throw new Error(
    `No ${path.basename(root).toLowerCase().slice(0, -1)} named "${name}" in ${root}.`
  );
}

// oxlint-disable no-magic-numbers -- the offsets of the zip format's records, as its specification
// numbers them.

/**
 * Reads an entry of a zip archive that stores it uncompressed, as the game's archives do.
 */
async function readStoredZipEntry(
  archive: FileHandle,
  archivePath: string,
  entryName: string
): Promise<Uint8Array> {
  const { size } = await archive.stat();
  // PK\x05\x06, little-endian.
  const endOfDirectorySignature = 101_010_256;

  // The end of central directory record: 22 bytes, then a comment of up to 64 KiB.
  const tailStart = Math.max(0, size - 22 - 65_535);
  const tail = await readRange(archive, size, tailStart, size);

  let end = tail.byteLength - 22;

  while (end >= 0 && tail.getUint32(end, true) != endOfDirectorySignature) {
    end--;
  }

  if (end < 0) {
    throw new Error(`${archivePath} is not a zip archive.`);
  }

  const directoryOffset = tail.getUint32(end + 16, true);
  const directorySize = tail.getUint32(end + 12, true);
  const directory = await readRange(
    archive,
    size,
    directoryOffset,
    directoryOffset + directorySize
  );

  let offset = 0;

  while (offset + 46 <= directory.byteLength) {
    const nameLength = directory.getUint16(offset + 28, true);
    const name = new TextDecoder().decode(
      new Uint8Array(directory.buffer, directory.byteOffset + offset + 46, nameLength)
    );

    if (name == entryName) {
      break;
    }

    offset +=
      46 +
      nameLength +
      directory.getUint16(offset + 30, true) +
      directory.getUint16(offset + 32, true);
  }

  if (offset + 46 > directory.byteLength) {
    throw new Error(`${archivePath} holds no ${entryName}.`);
  }

  if (directory.getUint16(offset + 10, true) != 0) {
    throw new Error(`${archivePath} compresses ${entryName}, which this reader does not read.`);
  }

  const entrySize = directory.getUint32(offset + 20, true);
  const headerOffset = directory.getUint32(offset + 42, true);
  const header = await readRange(archive, size, headerOffset, headerOffset + 30);
  const dataStart = headerOffset + 30 + header.getUint16(26, true) + header.getUint16(28, true);
  const data = await readRange(archive, size, dataStart, dataStart + entrySize);

  return new Uint8Array(data.buffer, data.byteOffset, data.byteLength);
}

// oxlint-enable no-magic-numbers

async function readRange(
  archive: FileHandle,
  size: number,
  start: number,
  end: number
): Promise<DataView> {
  // A damaged archive names any range, and the buffer below would be that large.
  if (end > size) {
    throw new Error(`The archive is damaged: it holds no bytes ${start} to ${end}.`);
  }

  const buffer = new Uint8Array(end - start);

  await archive.read(buffer, 0, buffer.length, start);

  return new DataView(buffer.buffer);
}
