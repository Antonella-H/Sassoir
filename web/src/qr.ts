const qrVersion = 6;
const qrSize = qrVersion * 4 + 17;
const qrDataCodewords = 136;
const qrBlockCount = 2;
const qrBlockDataCodewords = qrDataCodewords / qrBlockCount;
const qrEcCodewordsPerBlock = 18;
const qrRemainderBits = 7;
const qrMaskPattern = 0;

type QrMatrix = {
  modules: boolean[][];
  reserved: boolean[][];
};

export function createQrSvg(value: string) {
  const matrix = createQrMatrix(value);
  const quietZone = 4;
  const viewBoxSize = qrSize + quietZone * 2;
  const rects: string[] = [];

  matrix.forEach((row, y) => {
    row.forEach((dark, x) => {
      if (dark) rects.push(`<rect x="${x + quietZone}" y="${y + quietZone}" width="1" height="1"/>`);
    });
  });

  return [
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${viewBoxSize} ${viewBoxSize}" shape-rendering="crispEdges" role="img" aria-label="Event QR code">`,
    `<rect width="${viewBoxSize}" height="${viewBoxSize}" fill="#fff"/>`,
    `<g fill="#000">${rects.join("")}</g>`,
    "</svg>",
  ].join("");
}

export function createQrDataUri(value: string) {
  return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(createQrSvg(value))}`;
}

function createQrMatrix(value: string) {
  const { modules, reserved } = createEmptyMatrix();
  drawFunctionPatterns(modules, reserved);

  const bits = createDataCodewords(value)
    .flatMap((codeword) => byteToBits(codeword))
    .concat(Array(qrRemainderBits).fill(false));

  placeDataBits(modules, reserved, bits);
  drawFormatBits(modules, reserved);

  return modules;
}

function createEmptyMatrix(): QrMatrix {
  return {
    modules: Array.from({ length: qrSize }, () => Array(qrSize).fill(false)),
    reserved: Array.from({ length: qrSize }, () => Array(qrSize).fill(false)),
  };
}

function createDataCodewords(value: string) {
  const bytes = Array.from(new TextEncoder().encode(value));
  const maximumBytes = Math.floor((qrDataCodewords * 8 - 16) / 8);
  if (bytes.length > maximumBytes) {
    throw new Error("The event URL is too long to fit in the built-in QR template.");
  }

  const bits = [false, true, false, false, ...numberToBits(bytes.length, 8), ...bytes.flatMap(byteToBits)];
  bits.push(...Array(Math.min(4, qrDataCodewords * 8 - bits.length)).fill(false));
  while (bits.length % 8 !== 0) bits.push(false);

  const data = bitsToBytes(bits);
  for (let padIndex = 0; data.length < qrDataCodewords; padIndex += 1) {
    data.push(padIndex % 2 === 0 ? 0xec : 0x11);
  }

  const blocks = Array.from({ length: qrBlockCount }, (_, index) => {
    const start = index * qrBlockDataCodewords;
    const block = data.slice(start, start + qrBlockDataCodewords);
    return { data: block, errorCorrection: reedSolomonRemainder(block, qrEcCodewordsPerBlock) };
  });

  const codewords: number[] = [];
  for (let index = 0; index < qrBlockDataCodewords; index += 1) {
    blocks.forEach((block) => codewords.push(block.data[index]));
  }
  for (let index = 0; index < qrEcCodewordsPerBlock; index += 1) {
    blocks.forEach((block) => codewords.push(block.errorCorrection[index]));
  }

  return codewords;
}

function drawFunctionPatterns(modules: boolean[][], reserved: boolean[][]) {
  drawFinder(modules, reserved, 0, 0);
  drawFinder(modules, reserved, qrSize - 7, 0);
  drawFinder(modules, reserved, 0, qrSize - 7);
  drawAlignment(modules, reserved, qrSize - 7, qrSize - 7);

  for (let index = 8; index < qrSize - 8; index += 1) {
    setFunctionModule(modules, reserved, index, 6, index % 2 === 0);
    setFunctionModule(modules, reserved, 6, index, index % 2 === 0);
  }

  setFunctionModule(modules, reserved, 8, qrVersion * 4 + 9, true);
  reserveFormatAreas(reserved);
}

function drawFinder(modules: boolean[][], reserved: boolean[][], left: number, top: number) {
  for (let y = -1; y <= 7; y += 1) {
    for (let x = -1; x <= 7; x += 1) {
      const xx = left + x;
      const yy = top + y;
      if (!isInMatrix(xx, yy)) continue;

      const dark = x >= 0 && x <= 6 && y >= 0 && y <= 6
        && (x === 0 || x === 6 || y === 0 || y === 6 || (x >= 2 && x <= 4 && y >= 2 && y <= 4));
      setFunctionModule(modules, reserved, xx, yy, dark);
    }
  }
}

function drawAlignment(modules: boolean[][], reserved: boolean[][], centerX: number, centerY: number) {
  for (let y = -2; y <= 2; y += 1) {
    for (let x = -2; x <= 2; x += 1) {
      const distance = Math.max(Math.abs(x), Math.abs(y));
      setFunctionModule(modules, reserved, centerX + x, centerY + y, distance !== 1);
    }
  }
}

function reserveFormatAreas(reserved: boolean[][]) {
  for (let index = 0; index <= 8; index += 1) {
    if (index !== 6) {
      reserved[8][index] = true;
      reserved[index][8] = true;
    }
  }

  for (let index = qrSize - 8; index < qrSize; index += 1) reserved[8][index] = true;
  for (let index = qrSize - 7; index < qrSize; index += 1) reserved[index][8] = true;
}

function placeDataBits(modules: boolean[][], reserved: boolean[][], bits: boolean[]) {
  let bitIndex = 0;
  let upward = true;

  for (let right = qrSize - 1; right > 0; right -= 2) {
    if (right === 6) right -= 1;

    for (let vertical = 0; vertical < qrSize; vertical += 1) {
      const y = upward ? qrSize - 1 - vertical : vertical;
      for (let offset = 0; offset < 2; offset += 1) {
        const x = right - offset;
        if (reserved[y][x]) continue;

        const rawBit = bits[bitIndex] ?? false;
        modules[y][x] = rawBit !== shouldMask(x, y);
        bitIndex += 1;
      }
    }

    upward = !upward;
  }
}

function drawFormatBits(modules: boolean[][], reserved: boolean[][]) {
  const bits = getFormatBits(1, qrMaskPattern);
  const bit = (index: number) => ((bits >>> index) & 1) !== 0;

  for (let index = 0; index <= 5; index += 1) setFunctionModule(modules, reserved, 8, index, bit(index));
  setFunctionModule(modules, reserved, 8, 7, bit(6));
  setFunctionModule(modules, reserved, 8, 8, bit(7));
  setFunctionModule(modules, reserved, 7, 8, bit(8));
  for (let index = 9; index < 15; index += 1) setFunctionModule(modules, reserved, 14 - index, 8, bit(index));

  for (let index = 0; index < 8; index += 1) setFunctionModule(modules, reserved, qrSize - 1 - index, 8, bit(index));
  for (let index = 8; index < 15; index += 1) setFunctionModule(modules, reserved, 8, qrSize - 15 + index, bit(index));
}

function getFormatBits(errorCorrectionLevel: number, maskPattern: number) {
  let data = (errorCorrectionLevel << 3) | maskPattern;
  let remainder = data << 10;
  const generator = 0b10100110111;

  for (let bit = 14; bit >= 10; bit -= 1) {
    if (((remainder >>> bit) & 1) !== 0) remainder ^= generator << (bit - 10);
  }

  return ((data << 10) | remainder) ^ 0b101010000010010;
}

function setFunctionModule(modules: boolean[][], reserved: boolean[][], x: number, y: number, dark: boolean) {
  modules[y][x] = dark;
  reserved[y][x] = true;
}

function shouldMask(x: number, y: number) {
  return (x + y) % 2 === 0;
}

function isInMatrix(x: number, y: number) {
  return x >= 0 && y >= 0 && x < qrSize && y < qrSize;
}

function numberToBits(value: number, length: number) {
  return Array.from({ length }, (_, index) => ((value >>> (length - 1 - index)) & 1) !== 0);
}

function byteToBits(value: number) {
  return numberToBits(value, 8);
}

function bitsToBytes(bits: boolean[]) {
  const bytes: number[] = [];
  for (let index = 0; index < bits.length; index += 8) {
    bytes.push(bits.slice(index, index + 8).reduce((value, bit) => (value << 1) | (bit ? 1 : 0), 0));
  }
  return bytes;
}

function reedSolomonRemainder(data: number[], degree: number) {
  const generator = reedSolomonGenerator(degree);
  const result = Array(degree).fill(0);

  data.forEach((byte) => {
    const factor = byte ^ result.shift()!;
    result.push(0);
    generator.slice(1).forEach((coefficient, index) => {
      result[index] ^= gfMultiply(coefficient, factor);
    });
  });

  return result;
}

function reedSolomonGenerator(degree: number) {
  let result = [1];
  for (let index = 0; index < degree; index += 1) {
    const next = Array(result.length + 1).fill(0);
    result.forEach((coefficient, coefficientIndex) => {
      next[coefficientIndex] ^= coefficient;
      next[coefficientIndex + 1] ^= gfMultiply(coefficient, gfPow(index));
    });
    result = next;
  }
  return result;
}

function gfPow(power: number) {
  let value = 1;
  for (let index = 0; index < power; index += 1) value = gfMultiply(value, 2);
  return value;
}

function gfMultiply(left: number, right: number) {
  let result = 0;
  let a = left;
  let b = right;

  while (b > 0) {
    if ((b & 1) !== 0) result ^= a;
    a <<= 1;
    if ((a & 0x100) !== 0) a ^= 0x11d;
    b >>>= 1;
  }

  return result;
}
