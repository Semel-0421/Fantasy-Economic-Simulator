// Plan 7D Task 2 (Claude): Korean for every RON string and scenario title/pitch.
// Keys are the exact English source strings. Node/good keys and {placeholders} are kept; after a key only
// particles that do not depend on the final consonant are used (에, 에서, 의, 까지, 도), so substituted
// names read correctly.
export const scenarioKo: Record<string, string> = {
  // Scenario list (web/src/data/scenarios.ts)
  'First Venture': '첫 장사',
  'Learn to trade: buy cheap, sell dear, deliver a contract.': '장사의 기본: 싸게 사서 비싸게 팔고, 계약을 납품하세요.',
  'Shares and Bonds': '주식과 채권',
  'Grow coins through company shares and bonds.': '회사 주식과 채권으로 돈을 불리세요.',
  'Licences and Trade Abroad': '면허와 해외 무역',
  'Licences and trade between nations.': '면허를 사고 나라 사이에서 무역하세요.',

  // s1_first_venture.ron
  'Start in k0n0r0c0v1 with 500 coins and an empty cart. Buy cheap g1_0 here, then take it to k0n0r0c0, one week away. Your first sale pays at least 1.5 times what you paid. After that sale, normal market prices apply.':
    '500코인과 빈 수레로 k0n0r0c0v1에서 시작해요. 여기서 g1_0 상품을 싸게 사서 한 주 거리인 k0n0r0c0까지 가져가세요. 첫 판매는 산 값의 1.5배 이상을 받아요. 그 뒤로는 평소 시세가 적용돼요.',
  'Make a profit on your first sale.': '첫 판매에서 이익 내기',
  'Deliver the tool order to k0n0r0c1.': '도구 주문을 k0n0r0c1에 납품하기',
  'Earn 1,000 coins of net profit.': '순이익 1,000코인 벌기',
  'Deliver {qty} crates of {good} to {dest} by week {deadline}. Promised reward: {reward} coins. Buy the goods in k0n0r0c0, then travel through k0n0r0 to reach the destination.':
    '{deadline}주까지 {good} {qty}상자를 {dest}에 납품하세요. 약속된 보상: {reward}코인. 상품은 k0n0r0c0에서 사고, k0n0r0 지역을 거쳐 목적지로 가세요.',
  'Robbers are waiting on the road from k0n0r0 to k0n0r0c1. Guards reduce the attack chance by 70%. Insurance refunds 80% of lost cargo value. Choose your protection before you leave.':
    'k0n0r0에서 k0n0r0c1까지 가는 길에 도적이 기다리고 있어요. 호위를 쓰면 습격 확률이 70% 줄어요. 보험은 잃은 화물 값의 80%를 돌려줘요. 떠나기 전에 대비책을 고르세요.',
  'Delivery complete. Promised {reward} coins; you received {paid} coins.':
    '납품 완료. 약속된 보상은 {reward}코인, 받은 돈은 {paid}코인이에요.',

  // s2_shares_and_bonds.ron
  'Start at the bank in k0n0r0c0 with 10,000 coins. Buy company shares or national bonds. Share prices reflect buildings, cash and two years of earnings, divided among 100 shares. You may hold up to 49 shares per company. Your final wealth includes coins, deposits, shares at market price and bonds at face value.':
    '10,000코인을 들고 k0n0r0c0의 은행에서 시작해요. 회사 주식이나 국채를 사세요. 주가는 건물, 현금, 2년 치 수익을 100주로 나눈 값이에요. 한 회사의 주식은 49주까지 가질 수 있어요. 최종 재산에는 코인, 예금, 시가로 친 주식, 액면가로 친 채권이 들어가요.',
  'Earn 2,500 coins of net profit.': '순이익 2,500코인 벌기',
  'The tool workshop in k0n0r1c4 offers shares for about 85 coins each. A large order may be coming. Buy shares now, or keep your cash.':
    'k0n0r1c4의 도구 공방이 주식을 한 주에 85코인 정도에 내놓았어요. 큰 주문이 들어올지도 몰라요. 지금 주식을 살지, 현금을 지킬지 정하세요.',
  'The k0n0r1c4 workshop has won 6,000 coins of orders. Its cash and share price rise.':
    'k0n0r1c4 공방이 6,000코인어치 주문을 따냈어요. 공방의 현금과 주가가 올라요.',
  'The workshop pays 10 coins for each share you hold.': '공방이 가진 주식 한 주마다 10코인을 배당해요.',
  'The workshop may have lost a ship carrying its tools. If true, it could lose most of its cash. Sell your shares this week at the current price, or risk holding them.':
    '공방이 도구를 실은 배를 잃었을지도 몰라요. 사실이라면 현금 대부분을 잃을 수 있어요. 이번 주에 지금 가격으로 팔지, 위험을 안고 계속 가질지 정하세요.',
  'The workshop has lost 95% of its cash. Its shares now mainly reflect the value of its buildings.':
    '공방이 현금의 95%를 잃었어요. 이제 주가는 주로 건물 가치로 정해져요.',
  'Buy k0n0 bonds at the bank: 4% annual interest paid weekly into your deposit, plus a special 8% coupon in week 17. Your final wealth counts bonds at face value.':
    '은행에서 k0n0 국채를 사세요. 연 4% 이자가 매주 예금으로 들어오고, 17주에는 8% 특별 이자가 붙어요. 최종 재산에서 채권은 액면가로 계산해요.',
  'Review your bonds before the week 17 coupon. Keep them for the payment, or sell some if you need cash. Only bonds you still hold earn the coupon.':
    '17주 특별 이자 전에 채권을 점검하세요. 이자를 받으려면 그대로 두고, 현금이 필요하면 일부를 파세요. 그때 가진 채권에만 이자가 붙어요.',
  'Two weeks until the special 8% coupon. Buy more bonds, keep your cash, or sell. Check how much you can invest before placing an order.':
    '8% 특별 이자까지 2주 남았어요. 채권을 더 사거나, 현금을 지키거나, 파세요. 주문하기 전에 투자할 수 있는 금액을 확인하세요.',
  'k0n0 pays the special 8% coupon on the bonds you still hold.': 'k0n0에서 지금 가진 채권에 8% 특별 이자를 지급해요.',
  'Two weeks remain. Keep your investments or sell for cash. Your final wealth counts shares at market price and bonds at face value.':
    '2주 남았어요. 투자를 유지하거나 팔아서 현금으로 바꾸세요. 최종 재산에서 주식은 시가, 채권은 액면가로 계산해요.',

  // s3_licences_abroad.ron
  'Start in k0n0r0 with 30,000 coins. Buy g19_0 here and sell it in k0n1, where it is scarce. First buy the international trade licence here for 300 coins. Travel through k0n0 and k0 to k0n1, then buy that nation\'s licence for g19_0 before you sell. Return to k0n0r0 by week 24.':
    '30,000코인으로 k0n0r0에서 시작해요. 여기서 g19_0 상품을 사서, 그 상품이 귀한 k0n1에서 파세요. 먼저 여기서 국제 무역 면허를 300코인에 사세요. k0n0, k0 순서로 거쳐 k0n1까지 간 다음, 팔기 전에 그 나라의 g19_0 판매 면허를 사세요. 24주까지 k0n0r0에 돌아와야 해요.',
  'Earn your first coin of net profit.': '처음으로 순이익 내기',
  'Earn 1,000 coins of net profit after licence costs.': '면허 비용을 빼고 순이익 1,000코인 벌기',
  'Return to k0n0r0 by week 24.': '24주까지 k0n0r0에 돌아오기',
  'k0n1 is short of g19_0 and will pay much more for it over the next few weeks. Traders have limited cash: expect to sell about 40 units a week.':
    'k0n1에 g19_0 상품이 모자라서 앞으로 몇 주 동안 훨씬 비싸게 사 줘요. 상인들의 돈이 한정돼 있어서, 한 주에 40개 정도 팔 수 있어요.',
  'Robbers are watching the road from k0 to k0n1. Guards reduce the attack chance by 70%. Insurance refunds 80% of lost cargo value.':
    'k0에서 k0n1까지 가는 길을 도적이 노리고 있어요. 호위를 쓰면 습격 확률이 70% 줄어요. 보험은 잃은 화물 값의 80%를 돌려줘요.',
  'Buy the k0n1 licence for g19_0 before selling. Traders cannot buy this good from you without it.':
    '팔기 전에 k0n1의 g19_0 판매 면허를 사세요. 면허가 없으면 상인들이 이 상품을 사 줄 수 없어요.',
};
