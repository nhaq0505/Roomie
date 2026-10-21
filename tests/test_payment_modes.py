"""Real HTTP/DB coverage on harness-owned databases; never contacts VietQR/payOS."""
import concurrent.futures
import unittest
import uuid
from urllib.parse import urlsplit, parse_qs
from test_api_integration import ApiSession, OWNER_PASSWORD, ADMIN_EMAIL, ADMIN_PASSWORD, as_list, today_vietnam

class PaymentModesHttpTests(unittest.TestCase):
    def setUp(self):
        self.owner = ApiSession()
        self.email = f'payment-mode-{uuid.uuid4().hex}@example.invalid'
        self.owner.ok('POST', '/api/auth/register', {'name':'Payment mode fixture','email':self.email,'password':OWNER_PASSWORD,'phone':'0900000000'})
        self.owner.ok('POST', '/api/auth/login', {'email':self.email,'password':OWNER_PASSWORD})
        self.owner.ok('PUT','/api/settings/payment/preferences', {'confirmationMode':'Manual','bankBin':'970436','accountNumber':'12345678','accountName':'SYNTHETIC TEST ONLY'})
        building = self.owner.ok('POST','/api/buildings', {'name':'Payment fixture','address':'Disposable only'})
        self.room = self.owner.ok('POST','/api/rooms', {'buildingId':building['id'],'name':'Fixture room','rent':1000,'electricityRate':100,'waterFee':0,'waterPerPerson':False,'serviceFee':0,'initialReading':0})
        self.occupancy = self.owner.ok('POST',f"/api/rooms/{self.room['id']}/occupancy", {'tenantName':'Fixture tenant','people':1,'startDate':today_vietnam().replace(day=1).isoformat(),'depositAmount':500})['occupancy']
        self.token = self.occupancy['token']
        self.guest = ApiSession()
    def supplement(self, amount=10):
        return self.owner.ok('POST',f"/api/rooms/{self.room['id']}/supplemental-invoices", {'requestId':str(uuid.uuid4()),'occupancyId':self.occupancy['id'],'description':'Fixture charge','amount':amount,'dueDate':today_vietnam().isoformat()})
    def selection(self, invoice_id=None):
        return self.guest.ok('POST',f'/api/tenant/{self.token}/manual-payments', {'invoiceId':invoice_id})
    def test_all_pages_qr_and_idempotent_owner_confirmation(self):
        for _ in range(12): self.supplement()
        overview = self.guest.ok('GET',f'/api/tenant/{self.token}?page=1&pageSize=1')
        self.assertEqual(len(overview['invoices']),1)
        request = self.selection()
        self.assertEqual(request['amount'],overview['totals']['outstanding'])
        self.assertGreaterEqual(len(request['invoices']),13)
        self.assertEqual(self.selection()['id'],request['id'])
        self.assertTrue(self.guest.ok('GET',f"/api/tenant/{self.token}/manual-payments/{request['id']}")['canConfirm'])
        self.assertEqual(self.guest.request('GET',f"/api/tenant/invalid-fixture-token/manual-payments/{request['id']}")[0],404)
        listed = self.owner.ok('GET','/api/manual-payments?page=1&pageSize=1')
        self.assertEqual(len(listed['items']),1)
        self.assertEqual(listed['items'][0]['roomName'],self.room['name'])
        self.assertEqual(listed['items'][0]['tenantName'],'Fixture tenant')
        qr = urlsplit(request['qrUrl']); self.assertEqual(qr.hostname,'img.vietqr.io')
        self.assertEqual(int(parse_qs(qr.query)['amount'][0]),request['amount'])
        self.assertNotIn(self.token,request['qrUrl'])
        self.assertEqual(self.guest.request('POST',f"/api/manual-payments/{request['id']}/confirm",{'amount':request['amount']})[0],401)
        self.assertEqual(self.owner.request('POST',f"/api/manual-payments/{request['id']}/confirm",{'amount':1})[0],409)
        for _ in range(2): self.owner.ok('POST',f"/api/manual-payments/{request['id']}/confirm",{'amount':request['amount']})
        self.assertEqual(self.guest.ok('GET',f'/api/tenant/{self.token}')['totals']['outstanding'],0)
        pending = self.owner.ok('GET','/api/manual-payments?page=1&pageSize=1')
        self.assertEqual(pending['items'], [])
        self.assertEqual(pending['total'], 0)
    def test_stale_batch_cannot_partially_credit_other_invoices(self):
        charge = self.supplement(100)
        request = self.selection()
        self.owner.ok('POST',f"/api/invoices/{charge['id']}/receipts", {'amount':charge['total'],'note':'Fixture transfer','idempotencyKey':uuid.uuid4().hex})
        before = self.guest.ok('GET',f'/api/tenant/{self.token}')['totals']['outstanding']
        status,_,_=self.owner.request('POST',f"/api/manual-payments/{request['id']}/confirm", {'amount':request['amount']})
        self.assertEqual(status,409)
        self.assertEqual(self.guest.ok('GET',f'/api/tenant/{self.token}')['totals']['outstanding'],before)
    def test_parallel_confirmation_never_double_credits(self):
        self.supplement(100)
        request = self.selection()
        sessions=[ApiSession(),ApiSession()]
        for session in sessions: session.ok('POST','/api/auth/login',{'email':self.email,'password':OWNER_PASSWORD})
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            results=list(pool.map(lambda session: session.request('POST',f"/api/manual-payments/{request['id']}/confirm",{'amount':request['amount']})[0],sessions))
        self.assertIn(200,results)
        self.assertTrue(all(status in (200,409) for status in results),results)
        self.assertEqual(self.guest.ok('GET',f'/api/tenant/{self.token}')['totals']['outstanding'],0)
        self.owner.ok('POST',f"/api/manual-payments/{request['id']}/confirm",{'amount':request['amount']})
    def test_paid_elsewhere_is_review_only_and_dismissal_never_creates_receipts(self):
        charge = self.supplement(100)
        request = self.selection(charge['id'])
        self.owner.ok('POST',f"/api/invoices/{charge['id']}/receipts", {'amount':charge['total'],'note':'Already received','idempotencyKey':uuid.uuid4().hex})
        item = self.owner.ok('GET','/api/manual-payments')['items'][0]
        self.assertEqual(item['status'], 'AlreadyPaid')
        self.assertFalse(item['canConfirm']); self.assertTrue(item['canDismiss'])
        self.assertEqual(item['currentOutstanding'],0)
        self.assertEqual(self.guest.ok('GET',f"/api/tenant/{self.token}/manual-payments/{request['id']}")['status'],'AlreadyPaid')
        status,body,_ = self.owner.request('POST',f"/api/manual-payments/{request['id']}/confirm",{'amount':request['amount']})
        self.assertEqual(status,409); self.assertIn('đã được thu đủ',body['message'])
        self.assertEqual(self.guest.request('POST',f"/api/manual-payments/{request['id']}/dismiss")[0],401)
        other = ApiSession(); email=f'other-payment-{uuid.uuid4().hex}@example.invalid'
        other.ok('POST','/api/auth/register',{'name':'Other fixture','email':email,'password':OWNER_PASSWORD})
        other.ok('POST','/api/auth/login',{'email':email,'password':OWNER_PASSWORD})
        self.assertEqual(other.request('POST',f"/api/manual-payments/{request['id']}/dismiss")[0],404)
        before = self.owner.ok('GET',f"/api/invoices/{charge['id']}")
        for _ in range(2):
            dismissed = self.owner.ok('POST',f"/api/manual-payments/{request['id']}/dismiss")
            self.assertEqual(dismissed['status'],'Dismissed')
            self.assertIsNone(dismissed['confirmedAt']); self.assertIsNotNone(dismissed['dismissedAt'])
        self.assertEqual(self.owner.ok('GET','/api/manual-payments')['total'],0)
        self.assertEqual(self.guest.ok('GET',f"/api/tenant/{self.token}/manual-payments/{request['id']}")['status'],'Dismissed')
        self.assertEqual(self.owner.request('POST',f"/api/manual-payments/{request['id']}/confirm",{'amount':request['amount']})[0],409)
        after = self.owner.ok('GET',f"/api/invoices/{charge['id']}")
        self.assertEqual(after['paid'],before['paid']); self.assertEqual(after['version'],before['version'])
    def test_live_request_cannot_be_dismissed(self):
        request=self.selection()
        self.assertTrue(self.owner.ok('GET','/api/manual-payments')['items'][0]['canConfirm'])
        self.assertEqual(self.owner.request('POST',f"/api/manual-payments/{request['id']}/dismiss")[0],409)
        self.owner.ok('POST',f"/api/manual-payments/{request['id']}/confirm",{'amount':request['amount']})
        self.assertEqual(self.owner.ok('GET','/api/manual-payments')['total'],0)
    def test_changed_batch_can_be_replaced_without_losing_unpaid_members(self):
        charge=self.supplement(100)
        old=self.selection()
        self.owner.ok('POST',f"/api/invoices/{charge['id']}/receipts",{'amount':charge['total'],'note':'Received separately','idempotencyKey':uuid.uuid4().hex})
        fresh=self.selection()
        items=self.owner.ok('GET','/api/manual-payments')['items']
        stale=next(x for x in items if x['id']==old['id'])
        self.assertEqual(stale['status'],'InvoiceChanged'); self.assertFalse(stale['canConfirm'])
        self.assertEqual(stale['currentOutstanding'],fresh['amount'])
        self.owner.ok('POST',f"/api/manual-payments/{old['id']}/dismiss")
        remaining=self.owner.ok('GET','/api/manual-payments')
        self.assertEqual([x['id'] for x in remaining['items']],[fresh['id']])
        self.owner.ok('POST',f"/api/manual-payments/{fresh['id']}/confirm",{'amount':fresh['amount']})
        self.assertEqual(self.guest.ok('GET',f'/api/tenant/{self.token}')['totals']['outstanding'],0)
    def test_overlapping_requests_cannot_double_credit(self):
        charge=self.supplement(100)
        single=self.selection(charge['id']); batch=self.selection()
        self.owner.ok('POST',f"/api/manual-payments/{batch['id']}/confirm",{'amount':batch['amount']})
        remaining=self.owner.ok('GET','/api/manual-payments')
        self.assertEqual(remaining['items'][0]['id'],single['id'])
        self.assertEqual(remaining['items'][0]['status'],'AlreadyPaid')
        self.assertFalse(remaining['items'][0]['canConfirm'])
        self.assertEqual(self.owner.request('POST',f"/api/manual-payments/{single['id']}/confirm",{'amount':single['amount']})[0],409)
        invoice=self.owner.ok('GET',f"/api/invoices/{charge['id']}")
        self.assertEqual(invoice['paid'],charge['total']); self.assertEqual(invoice['outstanding'],0)
