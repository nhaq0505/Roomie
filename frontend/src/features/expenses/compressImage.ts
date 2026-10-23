/**
 * Shrinks photos in the browser before upload so receipts stay well under the 2 MB server limit.
 * PDFs and already-small images are sent unchanged; the server still verifies type and size.
 */
const maxEdge = 1600;
const passThroughBytes = 400 * 1024;

export async function prepareAttachment(file: File): Promise<{ blob: Blob; name: string }> {
  if (!file.type.startsWith('image/') || file.type === 'image/svg+xml') return { blob: file, name: file.name };
  if (typeof createImageBitmap !== 'function' || typeof document === 'undefined') return { blob: file, name: file.name };
  let bitmap: ImageBitmap;
  try { bitmap = await createImageBitmap(file); } catch { return { blob: file, name: file.name }; }
  const scale = Math.min(1, maxEdge / Math.max(bitmap.width, bitmap.height));
  if (scale === 1 && file.size <= passThroughBytes) { bitmap.close(); return { blob: file, name: file.name }; }
  const canvas = document.createElement('canvas');
  canvas.width = Math.max(1, Math.round(bitmap.width * scale));
  canvas.height = Math.max(1, Math.round(bitmap.height * scale));
  const context = canvas.getContext('2d');
  if (!context) { bitmap.close(); return { blob: file, name: file.name }; }
  context.fillStyle = '#ffffff';
  context.fillRect(0, 0, canvas.width, canvas.height);
  context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();
  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.8));
  if (!blob || blob.size >= file.size) return { blob: file, name: file.name };
  const base = file.name.replace(/\.[^.]+$/, '') || 'chung-tu';
  return { blob, name: `${base}.jpg` };
}
