import assert from 'node:assert/strict';
import vm from 'node:vm';
import { build } from 'esbuild';

const bundled = await build({
  entryPoints: ['src/features/rooms/cccdQr.ts'],
  bundle: true,
  write: false,
  format: 'cjs',
  platform: 'node',
  external: ['jsqr'],
});

const module = { exports: {} };
vm.runInNewContext(bundled.outputFiles[0].text, {
  module,
  exports: module.exports,
  require: () => {
    throw new Error('jsqr not needed for parser unit tests');
  },
  Date,
  Math,
  Number,
  String,
  RegExp,
  Array,
  Object,
});

const { parseCccdQr, parseCccdDate, normalizeCccdGender } = module.exports;

// 1. Standard 7-field CCCD with old 9-digit CMND
{
  const raw = '079098001234|025123456|Lê Thị Mai Anh|24121998|Nữ|88 Nguyễn Huệ, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh|10092022';
  const parsed = parseCccdQr(raw);
  assert.ok(parsed, 'Should parse standard 7-field CCCD');
  assert.equal(parsed.citizenId, '079098001234');
  assert.equal(parsed.oldIdNumber, '025123456');
  assert.equal(parsed.fullName, 'Lê Thị Mai Anh');
  assert.equal(parsed.dateOfBirth, '1998-12-24');
  assert.equal(parsed.gender, 'Nữ');
  assert.equal(parsed.permanentAddress, '88 Nguyễn Huệ, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh');
  assert.equal(parsed.issuedDate, '2022-09-10');
}

// 2. 7-field CCCD with empty old CMND field
{
  const raw = '001200012345||Trần Minh Quân|05052000|Nam|Số 10 Láng Hạ, Đống Đa, Hà Nội|12102021';
  const parsed = parseCccdQr(raw);
  assert.ok(parsed, 'Should parse 7-field CCCD with empty old CMND');
  assert.equal(parsed.citizenId, '001200012345');
  assert.equal(parsed.oldIdNumber, null);
  assert.equal(parsed.fullName, 'Trần Minh Quân');
  assert.equal(parsed.dateOfBirth, '2000-05-05');
  assert.equal(parsed.gender, 'Nam');
  assert.equal(parsed.issuedDate, '2021-10-12');
}

// 3. 6-field new Căn cước format (without old CMND column)
{
  const raw = '079204009988|Phạm Quốc Bảo|01012004|Nam|Quận 7, TP. Hồ Chí Minh|15072024';
  const parsed = parseCccdQr(raw);
  assert.ok(parsed, 'Should parse 6-field CCCD without old CMND column');
  assert.equal(parsed.citizenId, '079204009988');
  assert.equal(parsed.oldIdNumber, null);
  assert.equal(parsed.fullName, 'Phạm Quốc Bảo');
  assert.equal(parsed.dateOfBirth, '2004-01-01');
  assert.equal(parsed.gender, 'Nam');
  assert.equal(parsed.permanentAddress, 'Quận 7, TP. Hồ Chí Minh');
  assert.equal(parsed.issuedDate, '2024-07-15');
}

// 4. BOM, multiline, and whitespace resilience
{
  const raw = '\uFEFF  \n  079095001122 | 024998877 | Nguyễn Văn An | 15/08/1995 | nu | Đà Nẵng | 20/05/2021 \n ';
  const parsed = parseCccdQr(raw);
  assert.ok(parsed, 'Should handle BOM, newlines, and slash dates');
  assert.equal(parsed.citizenId, '079095001122');
  assert.equal(parsed.fullName, 'Nguyễn Văn An');
  assert.equal(parsed.dateOfBirth, '1995-08-15');
  assert.equal(parsed.gender, 'Nữ');
  assert.equal(parsed.issuedDate, '2021-05-20');
}

// 5. Date & gender edge cases
{
  assert.equal(parseCccdDate('29022024'), '2024-02-29', 'Valid leap year date');
  assert.equal(parseCccdDate('31022024'), null, 'Invalid Feb 31 date must return null');
  assert.equal(parseCccdDate('99999999'), null, 'Out-of-range date must return null');
  assert.equal(normalizeCccdGender('NỮ'), 'Nữ');
  assert.equal(normalizeCccdGender('nam'), 'Nam');
  assert.equal(normalizeCccdGender('unknown'), null);
}

// 6. Invalid QR payloads rejected safely
{
  assert.equal(parseCccdQr(''), null);
  assert.equal(parseCccdQr('https://roomie.io.vn/t/token123'), null);
  assert.equal(parseCccdQr('12345|025123456|Tên Ngắn|15081995|Nam|Địa chỉ|20052021'), null, 'Short citizenId rejected');
  assert.equal(parseCccdQr('079098001234|025123456||15081995|Nam|Địa chỉ|20052021'), null, 'Missing fullName rejected');
}

console.log('PASS: CCCD QR parser verified across 7-field, 6-field, BOM/whitespace, date validation, gender normalization, and invalid inputs.');
