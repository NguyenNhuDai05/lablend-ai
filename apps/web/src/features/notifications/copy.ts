const messages: Record<string, { title: string; message: string }> = {
  LoanSubmitted: { title: 'Đã gửi yêu cầu mượn', message: 'Hệ thống đã ghi nhận yêu cầu. Mở phiếu để theo dõi quá trình xử lý.' },
  LoanApproved: { title: 'Phiếu mượn đã được duyệt', message: 'Nhân viên đã duyệt yêu cầu mượn. Mở phiếu để xem thiết bị được cấp và hạn trả.' },
  LoanRejected: { title: 'Yêu cầu mượn bị từ chối', message: 'Nhân viên đã từ chối yêu cầu. Lý do được hiển thị trong chi tiết phiếu.' },
  LoanHandedOver: { title: 'Đã bàn giao thiết bị', message: 'Nhân viên đã xác nhận bàn giao. Kiểm tra danh sách thiết bị và hạn trả trong phiếu.' },
  LoanReturned: { title: 'Đã ghi nhận trả thiết bị', message: 'Nhân viên đã ghi nhận một lần nhận trả. Mở phiếu để xem tình trạng từng thiết bị và các mục còn chưa trả.' },
  LoanDueSoon: { title: 'Sắp đến hạn trả thiết bị', message: 'Hệ thống đã gửi lời nhắc trước hạn. Mở phiếu để kiểm tra hạn trả và thiết bị còn đang mượn.' },
  LoanOverdue: { title: 'Thiết bị đã quá hạn trả', message: 'Hệ thống đã ghi nhận thiết bị chưa trả sau hạn. Mở phiếu để kiểm tra tình trạng mới nhất và liên hệ phòng lab.' },
};

// Translate a persisted event; never synthesize notifications, dates, counts or lifecycle state.
export function notificationCopy(item: { eventType: string; title: string; message: string }) {
  return messages[item.eventType] ?? { title: item.title, message: item.message };
}
