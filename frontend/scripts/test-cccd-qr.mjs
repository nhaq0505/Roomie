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
  supported: { 'dynamic-import': false },
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

const { parseCccdQr, parsePassportMrz, computeMrzCheckDigit, parseCccdDate, normalizeCccdGender } = module.exports;

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
  assert.equal(parseCccdDate(null), null);
  assert.equal(parseCccdDate(''), null);
  assert.equal(parseCccdDate('   '), null);
  assert.equal(parseCccdDate('01011900'), '1900-01-01', 'Lower year boundary 1900 accepted');
  assert.equal(parseCccdDate('31122100'), '2100-12-31', 'Upper year boundary 2100 accepted');
  assert.equal(parseCccdDate('31121899'), null, 'Year 1899 rejected');
  assert.equal(parseCccdDate('01012101'), null, 'Year 2101 rejected');
  assert.equal(parseCccdDate('29022024'), '2024-02-29', 'Valid leap year date');
  assert.equal(parseCccdDate('29022023'), null, 'Non-leap year 2023-02-29 rejected');
  assert.equal(parseCccdDate('29/02/1900'), null, 'Century non-leap year 1900-02-29 rejected');
  assert.equal(parseCccdDate('31022024'), null, 'Invalid Feb 31 date must return null');
  assert.equal(parseCccdDate('31/04/2024'), null, 'Invalid Apr 31 date must return null');
  assert.equal(parseCccdDate('99999999'), null, 'Out-of-range date must return null');
  assert.equal(parseCccdDate('5-6-2001'), '2001-06-05', 'Single-digit day/month D-M-YYYY accepted');
  assert.equal(parseCccdDate('2001-6-5'), '2001-06-05', 'YYYY-M-D format accepted');
  assert.equal(parseCccdDate('2001/12/31'), '2001-12-31', 'YYYY/MM/DD format accepted');

  assert.equal(normalizeCccdGender('NỮ'), 'Nữ');
  assert.equal(normalizeCccdGender('nu'), 'Nữ');
  assert.equal(normalizeCccdGender('Female'), 'Nữ');
  assert.equal(normalizeCccdGender('f'), 'Nữ');
  assert.equal(normalizeCccdGender('nam'), 'Nam');
  assert.equal(normalizeCccdGender('Male'), 'Nam');
  assert.equal(normalizeCccdGender('M'), 'Nam');
  assert.equal(normalizeCccdGender('unknown'), null);
  assert.equal(normalizeCccdGender(''), null);
  assert.equal(normalizeCccdGender(null), null);
}

// 6. 9-digit ID, internal spaces in CitizenId, multi-line header text, and malformed DOB fallback in 7-field QR
{
  const multiLine = parseCccdQr('HEADER WITHOUT PIPE\n 025 123 456 ||Hoàng Minh Tuấn|1995-04-30|Male|Quận 3, TP.HCM|2020/10/05\nFOOTER');
  assert.ok(multiLine, 'Should parse 9-digit CitizenId on second line of multi-line input');
  assert.equal(multiLine.citizenId, '025123456');
  assert.equal(multiLine.fullName, 'Hoàng Minh Tuấn');
  assert.equal(multiLine.dateOfBirth, '1995-04-30');
  assert.equal(multiLine.gender, 'Nam');
  assert.equal(multiLine.issuedDate, '2020-10-05');

  const malformedDob7 = parseCccdQr('079098001234|025123456|Hoàng Thị Lan|bad-dob|Female|Bình Thạnh|10092022');
  assert.ok(malformedDob7, '7-field QR with malformed DOB should still extract name, gender, and address');
  assert.equal(malformedDob7.fullName, 'Hoàng Thị Lan');
  assert.equal(malformedDob7.dateOfBirth, null);
  assert.equal(malformedDob7.gender, 'Nữ');
  assert.equal(malformedDob7.issuedDate, '2022-09-10');
}

// 7. Invalid QR payloads rejected safely
{
  assert.equal(parseCccdQr(''), null);
  assert.equal(parseCccdQr('https://roomie.io.vn/t/token123'), null);
  assert.equal(parseCccdQr('12345|025123456|Tên Ngắn|15081995|Nam|Địa chỉ|20052021'), null, 'Short citizenId (< 9 digits) rejected');
  assert.equal(parseCccdQr('0123456789012|025123456|Tên Dài|15081995|Nam|Địa chỉ|20052021'), null, 'Long citizenId (> 12 digits) rejected');
  assert.equal(parseCccdQr('07909800123A|025123456|Có Chữ|15081995|Nam|Địa chỉ|20052021'), null, 'Alphanumeric citizenId rejected');
  assert.equal(parseCccdQr('079098001234|only|four|parts'), null, 'Fewer than 5 pipe-separated fields rejected');
  assert.equal(parseCccdQr('079098001234|p1|p2|p3|p4'), null, '5 fields without valid DOB rejected');
  assert.equal(parseCccdQr('079098001234|025123456||15081995|Nam|Địa chỉ|20052021'), null, 'Missing fullName in 7-field rejected');
  assert.equal(parseCccdQr('079098001234|   |15081995|Nam|Địa chỉ|20052021'), null, 'Blank fullName in 6-field rejected');

  // 7b. ICAO 9303 TD3 (2x44) Passport MRZ parsing & check-digit verification
  assert.equal(computeMrzCheckDigit('M87654321'), '4');
  assert.equal(computeMrzCheckDigit('950815'), '2');
  assert.equal(computeMrzCheckDigit('320814'), '0');
  assert.equal(computeMrzCheckDigit('C12345678'), '8');
  assert.equal(computeMrzCheckDigit('TK7654321<<'), '9');

  const korLine1 = 'P<KORKIM<<MIN<JUN<<<<<<<<<<<<<<<<<<<<<<<<<<<';
  const korLine2 = 'M876543214KOR9508152M32081401234567<<<<<<<00';
  const korParsed = parsePassportMrz(`${korLine1}\n${korLine2}`);
  assert.ok(korParsed, 'Should parse valid Korean TD3 Passport MRZ');
  assert.equal(korParsed.documentType, 'Passport');
  assert.equal(korParsed.citizenId, 'M87654321');
  assert.equal(korParsed.fullName, 'KIM MIN JUN');
  assert.equal(korParsed.dateOfBirth, '1995-08-15');
  assert.equal(korParsed.gender, 'Nam');
  assert.equal(korParsed.nationality, 'Hàn Quốc (KOR)');
  assert.equal(korParsed.permanentAddress, 'Quốc tịch: Hàn Quốc (KOR)');
  assert.equal(korParsed.issuedDate, null);

  const usaLine1 = 'P<USASMITH<<EMILY<ROSE<<<<<<<<<<<<<<<<<<<<<<';
  const usaLine2 = 'C123456788USA9812242F3112238<<<<<<<<<<<<<<00';
  const usaParsed = parseCccdQr(`${usaLine1}${usaLine2}`);
  assert.ok(usaParsed, 'parseCccdQr should fall back to parsePassportMrz for 88-char US MRZ');
  assert.equal(usaParsed.documentType, 'Passport');
  assert.equal(usaParsed.citizenId, 'C12345678');
  assert.equal(usaParsed.fullName, 'SMITH EMILY ROSE');
  assert.equal(usaParsed.dateOfBirth, '1998-12-24');
  assert.equal(usaParsed.gender, 'Nữ');
  assert.equal(usaParsed.nationality, 'Hoa Kỳ (USA)');
  assert.equal(usaParsed.permanentAddress, 'Quốc tịch: Hoa Kỳ (USA)');

  const jpnLine1 = 'P<JPNTANAKA<<KENJI<<<<<<<<<<<<<<<<<<<<<<<<<<';
  const jpnLine2 = 'TK76543219JPN0105101M3005109<<<<<<<<<<<<<<00';
  const jpnParsed = parseCccdQr(`${jpnLine1}\r\n${jpnLine2}`);
  assert.ok(jpnParsed, 'parseCccdQr should parse Japanese Passport MRZ with 2-letter prefix TK7654321');
  assert.equal(jpnParsed.citizenId, 'TK7654321');
  assert.equal(jpnParsed.fullName, 'TANAKA KENJI');
  assert.equal(jpnParsed.dateOfBirth, '2001-05-10');
  assert.equal(jpnParsed.gender, 'Nam');
  assert.equal(jpnParsed.nationality, 'Nhật Bản (JPN)');
  assert.equal(jpnParsed.permanentAddress, 'Quốc tịch: Nhật Bản (JPN)');

  const badPassportCheck = 'M876543215KOR9508152M32081401234567<<<<<<<00';
  assert.equal(parsePassportMrz(`${korLine1}\n${badPassportCheck}`), null, 'Tampered passport check digit rejected');

  const badDobCheck = 'M876543214KOR9508159M32081401234567<<<<<<<00';
  assert.equal(parsePassportMrz(`${korLine1}\n${badDobCheck}`), null, 'Tampered DOB check digit rejected');

  const badExpiryCheck = 'M876543214KOR9508152M32081491234567<<<<<<<00';
  assert.equal(parsePassportMrz(`${korLine1}\n${badExpiryCheck}`), null, 'Tampered expiry check digit rejected');
}

// 8. SEO metadataFor contract across 4 public pages and private/guest routes
{
  const seoBundled = await build({
    entryPoints: ['src/app/seo/site.ts'],
    bundle: true,
    write: false,
    format: 'cjs',
    platform: 'node',
  });
  const seoMod = { exports: {} };
  vm.runInNewContext(seoBundled.outputFiles[0].text, { module: seoMod, exports: seoMod.exports, RegExp, Object, Array, String });
  const { metadataFor, publicPages } = seoMod.exports;

  assert.equal(publicPages.length, 4, '4 public landing pages registered');
  for (const page of publicPages) {
    for (const candidate of page.path === '/' ? ['/'] : [page.path, `${page.path}/`]) {
      const meta = metadataFor(candidate);
      assert.equal(meta.public, true, `${candidate} is public`);
      assert.equal(meta.canonical, `https://roomie.io.vn${page.path}`, `${candidate} canonical matches origin`);
    }
  }

  for (const privatePath of [
    '/login',
    '/dashboard',
    '/rooms/room-1/tenant',
    '/rooms/room-1/documents',
    '/admin/owners',
    '/admin/monitoring',
    '/admin/promotions',
    '/settings/zalo',
    '/t/tok123',
    '/t/tok123/invoices/inv1/checkout',
    '/non-existent-route',
  ]) {
    const meta = metadataFor(privatePath);
    assert.equal(meta.public, false, `${privatePath} is private`);
    assert.equal(meta.canonical, null, `${privatePath} has null canonical`);
  }
  assert.equal(metadataFor('/non-existent-route').title, 'Không tìm thấy trang | Roomie');
}

// 9. Video frame & image blob QR decoding (BarcodeDetector + jsQR fallback + contrast enhancement) and legalTemplates
{
  let jsQrResponder = () => null;
  const envState = {
    window: undefined,
    document: undefined,
    URL: {
      created: [],
      revoked: [],
      createObjectURL(blob) {
        const u = `blob:mock-${this.created.length + 1}`;
        this.created.push({ u, blob });
        return u;
      },
      revokeObjectURL(u) {
        this.revoked.push(u);
      },
    },
    Image: class MockImage {
      constructor() {
        this.naturalWidth = 1600;
        this.naturalHeight = 1200;
      }
      set src(val) {
        this._src = val;
        if (envState.imageShouldError) {
          setTimeout(() => this.onerror && this.onerror(new Error('bad image')), 0);
        } else {
          setTimeout(() => this.onload && this.onload(), 0);
        }
      }
    },
    imageShouldError: false,
  };

  const decodeMod = { exports: {} };
  vm.runInNewContext(bundled.outputFiles[0].text, {
    module: decodeMod,
    exports: decodeMod.exports,
    require: (id) => {
      if (id === 'jsqr') {
        return (data, w, h, opts) => jsQrResponder(data, w, h, opts);
      }
      throw new Error(`Unexpected require: ${id}`);
    },
    get window() {
      return envState.window;
    },
    get document() {
      return envState.document;
    },
    get URL() {
      return envState.URL;
    },
    get Image() {
      return envState.Image;
    },
    Date,
    Math,
    Number,
    String,
    RegExp,
    Array,
    Object,
    Promise,
    Error,
    Uint8ClampedArray,
  });

  const { decodeCccdQrFromVideoFrame, decodeCccdQrFromImageBlob } = decodeMod.exports;
  const sampleQrText = '079098001234|025123456|Lê Thị Mai Anh|24121998|Nữ|Quận 1, TP.HCM|10092022';

  // 9a. 0x0 video frame returns null immediately
  assert.equal(await decodeCccdQrFromVideoFrame({ videoWidth: 0, videoHeight: 0 }, {}), null);

  // 9b. BarcodeDetector success path on video frame
  envState.window = {
    BarcodeDetector: class {
      async detect() {
        return [{ rawValue: 'not-a-cccd-qr' }, { rawValue: sampleQrText }];
      }
    },
  };
  const fromDetector = await decodeCccdQrFromVideoFrame({ videoWidth: 1920, videoHeight: 1080 }, {});
  assert.ok(fromDetector);
  assert.equal(fromDetector.citizenId, '079098001234');

  // 9c. BarcodeDetector throws -> falls back to jsQR with downscaled canvas (maxDim 960)
  envState.window = {
    BarcodeDetector: class {
      async detect() {
        throw new Error('BarcodeDetector unsupported format');
      }
    },
  };
  let drawnDims = null;
  const mockCanvas = {
    width: 0,
    height: 0,
    getContext() {
      return {
        drawImage(_v, _x, _y, w, h) {
          drawnDims = { w, h };
        },
        getImageData(_x, _y, w, h) {
          return { data: new Uint8ClampedArray(w * h * 4) };
        },
      };
    },
  };
  jsQrResponder = (_data, w, h) => (w === 960 && h === 540 ? { data: sampleQrText } : null);
  const fromJsQrVideo = await decodeCccdQrFromVideoFrame({ videoWidth: 1920, videoHeight: 1080 }, mockCanvas);
  assert.ok(fromJsQrVideo);
  assert.deepEqual(drawnDims, { w: 960, h: 540 }, 'Video frame 1920x1080 scales down to 960x540');
  assert.equal(fromJsQrVideo.fullName, 'Lê Thị Mai Anh');

  // 9c-2. When full-frame pass misses small dense QR, native-resolution center viewfinder crop (669x669) succeeds
  jsQrResponder = (_data, w, h) => (w === 669 && h === 669 ? { data: sampleQrText } : null);
  const fromCenterCropVideo = await decodeCccdQrFromVideoFrame({ videoWidth: 1920, videoHeight: 1080 }, mockCanvas);
  assert.ok(fromCenterCropVideo, 'Center viewfinder crop should decode small dense CCCD QR on mobile video');
  assert.equal(fromCenterCropVideo.citizenId, '079098001234');

  // 9d. decodeCccdQrFromImageBlob: invalid image rejects and revokes object URL
  envState.imageShouldError = true;
  await assert.rejects(() => decodeCccdQrFromImageBlob({}), /Invalid image/);
  assert.equal(envState.URL.created.length, 1);
  assert.equal(envState.URL.revoked.length, 1, 'Object URL revoked on image load error');
  envState.imageShouldError = false;

  // 9e. decodeCccdQrFromImageBlob: multi-region scan hits enhanceContrast pass (3rd region) via jsQR
  envState.window = undefined;
  let regionCall = 0;
  envState.document = {
    createElement(tag) {
      assert.equal(tag, 'canvas');
      return {
        width: 0,
        height: 0,
        getContext() {
          return {
            drawImage() {},
            getImageData(_x, _y, w, h) {
              const arr = new Uint8ClampedArray(w * h * 4);
              for (let i = 0; i < arr.length; i += 4) {
                const v = (i / 4) % 2 === 0 ? 40 : 220;
                arr[i] = v;
                arr[i + 1] = v;
                arr[i + 2] = v;
                arr[i + 3] = 255;
              }
              return { data: arr };
            },
          };
        },
      };
    },
  };
  jsQrResponder = (data) => {
    regionCall++;
    if (regionCall === 3) {
      assert.ok(data[0] < 40 && data[4] > 220, 'enhanceContrastInPlace stretched dark and light pixel luminance');
      return { data: sampleQrText };
    }
    return null;
  };
  const fromBlob = await decodeCccdQrFromImageBlob({});
  assert.ok(fromBlob);
  assert.equal(regionCall, 3);
  assert.equal(fromBlob.citizenId, '079098001234');

  // 9f. legalTemplates.ts helper functions
  const legalBundled = await build({
    entryPoints: ['src/shared/i18n/legalTemplates.ts'],
    bundle: true,
    write: false,
    format: 'cjs',
    platform: 'node',
  });
  const legalMod = { exports: {} };
  vm.runInNewContext(legalBundled.outputFiles[0].text, { module: legalMod, exports: legalMod.exports, Object });
  const { legalContractTemplate, legalCt01Template } = legalMod.exports;
  assert.equal(legalContractTemplate.todayAtAddress('23', '10', '2026', '123 Lê Lợi'), 'Hôm nay, ngày 23 tháng 10 năm 2026, tại địa chỉ: 123 Lê Lợi');
  assert.equal(legalContractTemplate.peopleCountText(3), 'Số người ở: 3 người');
  assert.equal(legalContractTemplate.coResidentsLine('Nguyễn B'), '- Thành viên ở cùng: Nguyễn B');
  assert.equal(legalContractTemplate.roomWithBuildingAddress('P.101', 'Dãy A', '10 Lê Lợi'), 'P.101, Dãy A, địa chỉ: 10 Lê Lợi');
  assert.ok(legalContractTemplate.clause1_2(25).includes('Diện tích sử dụng: 25 m².'));
  assert.ok(!legalContractTemplate.clause1_2(null).includes('Diện tích sử dụng:'));
  assert.equal(legalContractTemplate.inWordsText('Ba triệu đồng chẵn'), '(Bằng chữ: Ba triệu đồng chẵn)');
  assert.equal(legalCt01Template.item10Content('10 Lê Lợi'), 'Đăng ký tạm trú tại địa chỉ: 10 Lê Lợi.');
}

console.log('PASS: CCCD QR parser, video/image decoders, legal templates, and SEO metadata verified across all edge cases.');
