import { getLanguage, hasMessage, t, translateMessage } from './runtime';
// Known server messages map to localized recovery guidance. Provider messages never become UI HTML.
const messages: Record<string, readonly [string, string, string, string, string]> = {
 passwordLength: ["Mật khẩu mới cần từ 8 đến 256 ký tự.", "The new password must contain 8 to 256 characters.", "新密码须包含 8 至 256 个字符。", "새 비밀번호는 8~256자여야 합니다.", "नया पासवर्ड 8 से 256 अक्षरों का होना चाहिए।"],
 passwordDifferent: ["Mật khẩu mới phải khác mật khẩu hiện tại.", "The new password must differ from the current password.", "新密码必须与当前密码不同。", "새 비밀번호는 현재 비밀번호와 달라야 합니다.", "नया पासवर्ड वर्तमान पासवर्ड से अलग होना चाहिए।"],
 passwordConfirmation: ["Xác nhận mật khẩu không khớp.", "Passwords do not match.", "两次密码不一致。", "비밀번호가 일치하지 않습니다.", "पासवर्ड मेल नहीं खाते।"],
 currentPassword: ["Mật khẩu hiện tại không chính xác.", "The current password is incorrect.", "当前密码不正确。", "현재 비밀번호가 올바르지 않습니다.", "वर्तमान पासवर्ड गलत है।"],
 auth: ['Vui lòng đăng nhập.', 'Please sign in.', '请登录。', '로그인하세요.', 'कृपया लॉग इन करें।'],
 access: ['Tài khoản không có quyền thực hiện thao tác này.', 'Your account cannot perform this action.', '此账户没有操作权限。', '이 작업을 수행할 권한이 없습니다.', 'इस कार्य की अनुमति नहीं है।'],
 login: ['Email hoặc mật khẩu không chính xác.', 'Incorrect email or password.', '邮箱或密码不正确。', '이메일 또는 비밀번호가 올바르지 않습니다.', 'ईमेल या पासवर्ड गलत है।'],
 email: ['Vui lòng nhập địa chỉ email hợp lệ.', 'Enter a valid email address.', '请输入有效邮箱。', '올바른 이메일을 입력하세요.', 'मान्य ईमेल दर्ज करें।'],
 duplicateEmail: ['Email này đã được đăng ký.', 'This email is already registered.', '此邮箱已注册。', '이미 가입된 이메일입니다.', 'यह ईमेल पहले से पंजीकृत है।'],
 approved: ['Tài khoản chủ trọ chưa được duyệt để nhận thanh toán.', 'The owner account is not approved to accept payments.', '房东账户尚未获准收款。', '임대인 계정이 결제 수납 승인을 받지 않았습니다.', 'मकान मालिक के खाते को भुगतान लेने की स्वीकृति नहीं है।'],
 missing: ['Không tìm thấy dữ liệu được yêu cầu.', 'The requested record was not found.', '找不到请求的数据。', '요청한 데이터를 찾을 수 없습니다.', 'माँगा गया रिकॉर्ड नहीं मिला।'],
 inactiveLink: ['Link khách không còn hiệu lực. Vui lòng liên hệ chủ trọ.', 'This tenant link is inactive. Contact the owner.', '租客链接已失效，请联系房东。', '임차인 링크가 만료되었습니다. 임대인에게 문의하세요.', 'किरायेदार का लिंक निष्क्रिय है। मकान मालिक से संपर्क करें।'],
 occupied: ['Phòng đã có người thuê đang ở.', 'This room already has an active tenant.', '此房间已有租客。', '이미 입주 중인 방입니다.', 'इस कमरे में पहले से किरायेदार है।'],
 noTenant: ['Phòng chưa có người thuê đang ở.', 'This room has no active tenant.', '此房间尚无租客。', '현재 임차인이 없는 방입니다.', 'इस कमरे में वर्तमान किरायेदार नहीं है।'],
 stale: ['Lượt thuê đã thay đổi. Vui lòng tải lại danh sách phòng.', 'The tenancy changed. Reload the room list.', '租期已变更，请重新加载房间列表。', '임대가 변경되었습니다. 방 목록을 새로고침하세요.', 'किरायेदारी बदल गई है। कमरे की सूची फिर लोड करें।'],
 reading: ['Chỉ số mới phải không âm và không thấp hơn chỉ số đã chốt.', 'The reading must be non-negative and at least the last finalized reading.', '新读数须为非负数且不低于已确认读数。', '검침은 0 이상이며 마지막 확정 검침 이상이어야 합니다.', 'रीडिंग शून्य या अधिक और पिछली तय रीडिंग से कम नहीं होनी चाहिए।'],
 readingPeriod: ['Nhập chỉ số theo thứ tự tháng, không trước ngày thuê hoặc trong tương lai.', 'Enter readings in month order, not before move-in or for future months.', '请按月份顺序录入，不能早于入住月份或在未来。', '입주 이후 월 순서대로 입력하세요. 미래 월은 불가합니다.', 'रीडिंग माह के क्रम में दर्ज करें; प्रवेश से पहले या भविष्य के माह की नहीं।'],
 readingExists: ['Phòng đã có chỉ số hoặc hóa đơn trong kỳ này.', 'This room already has a reading or invoice for this period.', '此房间本期已有读数或账单。', '이번 기간 검침 또는 청구서가 이미 있습니다.', 'इस अवधि में कमरे की रीडिंग या बिल पहले से है।'],
 finalExists: ['Đã chốt điện trả phòng. Không thể nhập thêm chỉ số tháng cho lượt thuê này.', 'Move-out readings are finalized. No more monthly readings can be entered for this tenancy.', '已确认退房读数，本租期不能再录入月度读数。', '퇴거 검침 확정 후에는 이 임대의 월 검침을 추가할 수 없습니다.', 'कमरा छोड़ने की रीडिंग तय हो गई है। इस किरायेदारी में और मासिक रीडिंग नहीं जोड़ सकते।'],
 finalRequired: ['Cần chốt điện cuối kỳ trước khi kết thúc lượt thuê.', 'Finalize the last electricity reading before ending tenancy.', '结束租期前需确认最终电表读数。', '임대 종료 전에 최종 검침을 확정하세요.', 'किरायेदारी समाप्त करने से पहले अंतिम बिजली रीडिंग तय करें।'],
 depositHeld: ['Cần hoàn hoặc khấu trừ hết cọc đang giữ trước khi trả phòng.', 'Refund or deduct the remaining held deposit before move-out.', '退房前需退还或扣除全部保管押金。', '퇴거 전에 남은 보증금을 반환하거나 공제하세요.', 'कमरा छोड़ने से पहले शेष जमा राशि लौटाएँ या समायोजित करें।'],
 debt: ['Cần quyết toán hết công nợ trước khi trả phòng.', 'Settle all outstanding invoices before move-out.', '退房前需结清全部欠款。', '퇴거 전에 모든 미수금을 정산하세요.', 'कमरा छोड़ने से पहले सभी बकाया बिल निपटाएँ।'],
 pending: ['Có giao dịch payOS đang chờ. Đối soát trước khi tiếp tục.', 'A payOS payment is pending. Reconcile it before continuing.', '存在待处理 payOS 交易，请先核对。', '대기 중인 payOS 결제가 있습니다. 먼저 대사하세요.', 'payOS भुगतान लंबित है। आगे बढ़ने से पहले जाँचें।'],
 configured: ['Vui lòng lưu cấu hình payOS trước khi thanh toán hoặc đối soát.', 'Save payOS settings before payment or reconciliation.', '付款或核对前请先保存 payOS 配置。', '결제 또는 대사 전에 payOS 설정을 저장하세요.', 'भुगतान या मिलान से पहले payOS सेटिंग सहेजें।'],
 credentials: ['Kiểm tra và lưu lại thông tin kết nối payOS.', 'Check and save the payOS credentials again.', '请检查并重新保存 payOS 连接信息。', 'payOS 연결 정보를 확인하고 다시 저장하세요.', 'payOS की जानकारी जाँचकर फिर सहेजें।'],
 paid: ['Hóa đơn đã được thanh toán đầy đủ.', 'This invoice is already fully paid.', '此账单已全额付清。', '이미 완납된 청구서입니다.', 'इस बिल का पूरा भुगतान हो चुका है।'],
 cancelled: ['Giao dịch payOS trước đã hủy. Hãy bắt đầu thanh toán mới.', 'The previous payOS order was cancelled. Start a new payment.', '上次 payOS 订单已取消，请重新付款。', '이전 payOS 주문이 취소되었습니다. 새 결제를 시작하세요.', 'पिछला payOS आदेश रद्द हुआ। नया भुगतान शुरू करें।'],
 uncertain: ['Kết quả payOS chưa xác định. Đối soát giao dịch trước khi thử lại.', 'The payOS result is uncertain. Reconcile before retrying.', 'payOS 结果尚未确定，请核对后再重试。', 'payOS 결과가 불확실합니다. 재시도 전에 대사하세요.', 'payOS का परिणाम अनिश्चित है। फिर प्रयास करने से पहले जाँचें।'],
 balance: ['Số tiền vượt quá số dư hóa đơn hoặc cọc đang giữ.', 'The amount exceeds the invoice balance or deposit held.', '金额超过账单余额或保管押金。', '금액이 청구서 잔액 또는 보관 보증금을 초과합니다.', 'राशि बिल के बकाया या रखी जमा राशि से अधिक है।'],
 amount: ['Nhập số tiền nguyên VND hợp lệ trong giới hạn cho phép.', 'Enter a valid whole VND amount within the allowed limits.', '请输入限额内有效的整数 VND 金额。', '허용 범위의 올바른 정수 VND 금액을 입력하세요.', 'अनुमत सीमा में मान्य पूर्ण VND राशि दर्ज करें।'],
 duplicate: ['Mã thao tác đã dùng cho nội dung khác. Tải lại trước khi thử lại.', 'This operation ID was used for different data. Reload before retrying.', '操作编号已用于其他内容，请刷新后重试。', '작업 ID가 다른 내용에 사용되었습니다. 새로고침 후 다시 시도하세요.', 'यह कार्य ID अन्य डेटा के लिए उपयोग हुआ है। फिर प्रयास करने से पहले रिफ़्रेश करें।'],
 people: ['Số người ở phải từ 1 đến 30.', 'Residents must be between 1 and 30.', '居住人数须为 1 至 30。', '거주 인원은 1~30명이어야 합니다.', 'रहने वालों की संख्या 1 से 30 हो।'],
 futureDate: ['Ngày chuyển vào không thể trong tương lai.', 'Move-in date cannot be in the future.', '入住日期不能在未来。', '입주일은 미래 날짜일 수 없습니다.', 'प्रवेश तिथि भविष्य की नहीं हो सकती।'],
 name: ['Tên không được để trống hoặc vượt giới hạn ký tự.', 'The name is required and must be within the character limit.', '名称不能为空或超出字符限制。', '이름은 필수이며 글자 수 제한을 지켜야 합니다.', 'नाम आवश्यक है और अक्षरों की सीमा में होना चाहिए।'],
 reason: ['Nhập lý do hoặc ghi chú trong giới hạn ký tự.', 'Enter a reason or note within the character limit.', '请填写字符限制内的原因或备注。', '글자 수 제한 내에서 사유 또는 메모를 입력하세요.', 'अक्षरों की सीमा में कारण या टिप्पणी दर्ज करें।'],
 scope: ['Khoản quyết toán phải thuộc đúng người thuê và loại hóa đơn.', 'Settlement must match the tenant and invoice type.', '结算须属于相同租客及正确账单类型。', '정산은 해당 임차인과 청구 유형에 맞아야 합니다.', 'निपटान सही किरायेदार और बिल के प्रकार से मेल खाए।'],
 form: ['Kiểm tra các thông tin nhập và thử lại.', 'Check the entered details and try again.', '请检查输入信息后重试。', '입력 정보를 확인하고 다시 시도하세요.', 'दर्ज जानकारी जाँचकर फिर प्रयास करें।'],
 request: ['Thao tác chưa thành công. Vui lòng thử lại hoặc liên hệ hỗ trợ.', 'The action failed. Try again or contact support.', '操作失败，请重试或联系支持。', '작업에 실패했습니다. 다시 시도하거나 지원을 요청하세요.', 'कार्य विफल हुआ। फिर प्रयास करें या सहायता से संपर्क करें।'],
 server: ['Máy chủ chưa thể xử lý. Vui lòng thử lại sau.', 'The server cannot process this right now. Try again later.', '服务器暂时无法处理，请稍后重试。', '서버에서 처리할 수 없습니다. 나중에 다시 시도하세요.', 'सर्वर अभी संसाधित नहीं कर सकता। बाद में फिर प्रयास करें।'],
};
const rules: readonly [RegExp, string][] = [
 [/Mật khẩu hiện tại không chính xác/, 'currentPassword'],
 [/Mật khẩu mới cần từ 8 đến 256 ký tự/, 'passwordLength'],
 [/Xác nhận mật khẩu không khớp/, 'passwordConfirmation'],
 [/Mật khẩu mới phải khác mật khẩu hiện tại/, 'passwordDifferent'],
 [/Email hoặc mật khẩu/, 'login'], [/Email này đã/, 'duplicateEmail'], [/địa chỉ email/, 'email'],
 [/Please sign in/, 'auth'], [/access is required|cannot.*sign in|không thể đăng nhập/, 'access'],
 [/not approved/, 'approved'], [/Tenant link.*active/, 'inactiveLink'], [/not found|was not found|Không tìm thấy/, 'missing'],
 [/already has an active occupancy/, 'occupied'], [/no active occupancy|chưa có người thuê|người thuê đang ở/, 'noTenant'],
 [/đã thay đổi|Người thuê đã thay đổi/, 'stale'], [/không thể thấp hơn|không được thấp hơn|Chỉ số điện phải/, 'reading'],
 [/tháng.*tương lai|tháng bắt đầu thuê|thứ tự tháng/, 'readingPeriod'],
 [/đã có chỉ số|đã được phát hành|meter reading already exists/, 'readingExists'],
 [/Đã chốt điện trả phòng|đã có hóa đơn chốt/, 'finalExists'], [/Cần chốt điện cuối/, 'finalRequired'],
 [/hoàn hoặc khấu trừ.*cọc|hoàn hoặc khấu trừ toàn bộ/, 'depositHeld'], [/quyết toán hết công nợ|khấu trừ hết công nợ/, 'debt'],
 [/pending payOS|đang chờ|already being created|still being prepared/, 'pending'],
 [/before reconciling|not configured|lưu cấu hình payOS|thiết lập payOS/, 'configured'],
 [/credentials|API key|Client ID|API Key|Checksum Key|checksum key|xác thực payOS/, 'credentials'],
 [/already paid/, 'paid'], [/order was cancelled/, 'cancelled'], [/uncertain|invalid.*order status|unsigned payment link/, 'uncertain'],
 [/exceed.*balance|vượt.*cọc|vượt số tiền còn nợ/, 'balance'],
 [/amount must|amount cannot|supported limit|Tiền cọc phải|tiền vượt|số tiền nguyên VND|khoản bổ sung nguyên VND/, 'amount'],
 [/already used|Mã.*đã.*dùng/, 'duplicate'], [/People must/, 'people'], [/Start date/, 'futureDate'],
 [/name is required|họ tên tối đa/, 'name'], [/Reason must|Receipt note/, 'reason'],
 [/cùng người thuê|Hoàn cọc không thể/, 'scope'], [/Status must|Unknown owner|Period must|non-negative|Supply at least one|charge fields|amount field|meter or charge/, 'form'],
 [/Only a monthly draft|Only an issued invoice|recorded payment cannot|settlement records cannot|later meter reading|later move-out reading|editable meter anchor|source meter reading|Draft invoices must|Draft deposit invoices/, 'request'],
];
const languageIndex = () => ({ vi: 0, en: 1, zh: 2, ko: 3, hi: 4 })[getLanguage()];
export function errorText(source: string, status = 400) {
  const known = translateMessage(source);
  if (hasMessage(source)) return known;
  // Also refresh previously displayed localized error messages when the language changes.
  for (const row of Object.values(messages)) if (row.includes(source)) return row[languageIndex()];
  if (source === 'Mật khẩu cần có ít nhất 8 ký tự.') return t(source);
  for (const [pattern, key] of rules) if (pattern.test(source)) return messages[key][languageIndex()];
  if (getLanguage() === 'vi') return source;
  return messages[status >= 500 ? 'server' : status === 401 ? 'auth' : status === 403 ? 'access' : status === 404 ? 'missing' : 'request'][languageIndex()];
}
