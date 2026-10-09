namespace MSA.CatalogSeeder;

/// <param name="PriceStep">选这个属性里靠后的值时每档加价（存储、尺寸、规格）；颜色、尺码这类不影响价格的为 0。</param>
internal sealed record AttributeSpec(string Name, decimal PriceStep, params string[] Values);

/// <summary>
/// 一个分类的造数规则。商品名由 <see cref="Templates"/> 填 <see cref="Slots"/> 生成，
/// 词表按品类单独写，保证「品牌 + 系列 + 卖点 + 品名」读起来像真的商品，而不是随机拼字。
/// 模板里 {名字} 取词表，{#a-b} 取 a 到 b 的整数；词表里的空串表示这一段可以省略。
/// </summary>
internal sealed record CategorySpec(
    string Code,
    string Name,
    double Weight,
    string Glyph,
    int Hue,
    string PhotoKeyword,
    decimal MinPrice,
    decimal MaxPrice,
    string[] Templates,
    Dictionary<string, string[]> Slots,
    AttributeSpec[] Attributes,
    string[] Highlights);

internal static class Taxonomy
{
    private static readonly string[] DigitalBrands = ["星澜", "极光", "曜石", "北辰", "青柠", "遥光", "凌越", "木星"];
    private static readonly string[] AudioBrands = ["声阔", "律动", "回响", "听澜", "静屿", "弦音"];
    private static readonly string[] ApplianceBrands = ["美家", "暖居", "净界", "小厨", "安住", "悦享", "禾田"];
    private static readonly string[] ApparelBrands = ["木禾", "素衫", "城野", "拾光", "青衿", "布语", "野森", "初棉"];
    private static readonly string[] ShoeBrands = ["疾风", "跃动", "行者", "轻踏", "奔途"];
    private static readonly string[] BagBrands = ["途乐", "行囊", "拾光", "城野", "远行客"];
    private static readonly string[] BeautyBrands = ["花研", "植萃集", "澄肌", "柔沐", "研色", "初颜"];
    private static readonly string[] FoodBrands = ["山野食记", "果园派", "晨烘", "坚果日记", "鲜语"];
    private static readonly string[] TeaBrands = ["茶语", "山野食记", "一叶", "云雾间"];
    private static readonly string[] HomeBrands = ["栖居", "素居", "简屋", "棉朵", "暖居"];
    private static readonly string[] OutdoorBrands = ["野径", "远山", "风行者", "极地"];
    private static readonly string[] Colors = ["曜石黑", "冰川白", "远峰蓝", "暮光紫", "松岭青", "砂岩金"];
    private static readonly string[] ClothingColors = ["白色", "黑色", "浅灰", "藏青", "燕麦色", "雾霾蓝", "卡其"];
    private static readonly string[] ClothingSizes = ["S", "M", "L", "XL", "XXL"];
    private static readonly string[] Genders = ["男款", "女款", "男女同款"];

    /// <summary>所有品类词表里出现过的品牌（同一品牌可能横跨几个品类，例如「拾光」既卖衣服也卖包），按首次出现排序。</summary>
    public static IReadOnlyList<string> BrandNames => All
        .SelectMany(spec => spec.Slots["brand"])
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public static readonly string[] Promises =
    [
        "支持 7 天无理由退换。",
        "全国多仓发货，下单后 48 小时内发出。",
        "正品保障，可开具电子发票。",
        "官方质保一年，售后无忧。",
        "顺丰包邮，偏远地区除外。"
    ];

    public static readonly CategorySpec[] All =
    [
        new("PHONE", "手机", 6, "📱", 215, "smartphone", 799, 8999,
            ["{brand} {series} {#10-90}{suffix} {feature}{noun}"],
            new()
            {
                ["brand"] = DigitalBrands,
                ["series"] = ["Nova", "Neo", "Air", "Spark", "Edge", "Vista"],
                ["suffix"] = ["", "", " Pro", " Pro+", " Ultra", " SE", " Lite"],
                ["feature"] = ["5G", "影像旗舰", "长续航", "轻薄", "电竞", "折叠屏"],
                ["noun"] = ["手机", "智能手机"]
            },
            [new("颜色", 0, Colors), new("存储", 300, "8GB+128GB", "8GB+256GB", "12GB+256GB", "12GB+512GB", "16GB+1TB")],
            ["搭载新一代旗舰芯片，多任务切换流畅不卡顿", "5000mAh 大电池配合 100W 快充，一天一充", "后置多摄影像系统，夜景与人像表现出色",
             "IP68 防尘防水，日常更安心", "1.5K 高刷护眼屏，长时间观看不累眼"]),

        new("TABLET", "平板电脑", 2, "📲", 225, "tablet computer", 1299, 6999,
            ["{brand} {series} Pad {#5-12}{suffix} {feature}平板电脑"],
            new()
            {
                ["brand"] = DigitalBrands,
                ["series"] = ["Nova", "Air", "Edge", "Vista"],
                ["suffix"] = ["", " Pro", " Plus", " Mini"],
                ["feature"] = ["", "11英寸 ", "12.4英寸 ", "2.8K高刷", "护眼"]
            },
            [new("颜色", 0, "深空灰", "银色", "薄荷绿", "星光白"), new("存储", 400, "8GB+128GB", "8GB+256GB", "12GB+512GB"), new("网络", 500, "WLAN版", "5G版")],
            ["支持手写笔与键盘，学习办公两不误", "四扬声器环绕立体声，追剧更沉浸", "轻至 480g，单手握持不吃力", "平行视界多窗口，效率翻倍"]),

        new("LAPTOP", "笔记本电脑", 3, "💻", 205, "laptop", 3499, 14999,
            ["{brand} {series} {year} {size}{feature}笔记本电脑"],
            new()
            {
                ["brand"] = DigitalBrands,
                ["series"] = ["Book", "Book Pro", "Station", "Air", "Titan"],
                ["year"] = ["2025款", "2026款"],
                ["size"] = ["14英寸", "15.6英寸", "16英寸", "13.3英寸"],
                ["feature"] = ["轻薄", "高性能", "游戏", "商务", "创作"]
            },
            [new("配置", 800, "i5/16G/512G", "i7/16G/1T", "i7/32G/1T", "i9/32G/2T"), new("颜色", 0, "银色", "深空灰", "雾蓝")],
            ["2.8K OLED 屏，100% DCI-P3 广色域", "全金属机身，厚度仅 14.9mm", "满血独显，3A 大作流畅运行", "长效续航 14 小时，出差不带充电器"]),

        new("MONITOR", "显示器", 1.5, "🖥️", 200, "computer monitor", 699, 4999,
            ["{brand} {size} {res} {panel}{feature}显示器"],
            new()
            {
                ["brand"] = DigitalBrands,
                ["size"] = ["24英寸", "27英寸", "32英寸", "34英寸带鱼屏"],
                ["res"] = ["2K", "4K", "1080P"],
                ["panel"] = ["IPS", "Fast IPS", "OLED", "VA"],
                ["feature"] = ["电竞", "设计师", "护眼", ""]
            },
            [new("支架", 100, "标准支架", "升降旋转支架")],
            ["165Hz 高刷新率，1ms 响应", "出厂逐台校色，ΔE<2", "Type-C 一线连接，65W 反向供电", "硬件级低蓝光，莱茵认证"]),

        new("HEADPHONE", "耳机", 4, "🎧", 270, "headphones", 99, 2999,
            ["{brand} {series} {#2-9} {feature}{noun}"],
            new()
            {
                ["brand"] = AudioBrands,
                ["series"] = ["Buds", "Free", "Echo", "Pulse", "Studio"],
                ["feature"] = ["主动降噪", "真无线", "头戴式", "开放式", "运动", "电竞"],
                ["noun"] = ["蓝牙耳机", "耳机"]
            },
            [new("颜色", 0, "月光白", "石墨黑", "雾霾蓝", "樱花粉")],
            ["深度降噪 50dB，通勤路上一秒安静", "LDAC 高清音质，细节清晰", "单次续航 8 小时，配合充电盒 36 小时", "双设备同时连接，手机电脑无缝切换"]),

        new("SPEAKER", "蓝牙音箱", 1, "🔊", 280, "bluetooth speaker", 129, 2499,
            ["{brand} {series} {feature}蓝牙音箱"],
            new()
            {
                ["brand"] = AudioBrands,
                ["series"] = ["Boom", "Go", "Home", "Party"],
                ["feature"] = ["便携", "IPX7防水", "桌面", "重低音", "智能"]
            },
            [new("颜色", 0, "石墨黑", "月光白", "森林绿", "珊瑚红")],
            ["360° 环绕声场，派对一台就够", "IPX7 级防水，户外随身带", "两台互联组成立体声", "20 小时超长续航"]),

        new("WATCH", "智能手表", 2, "⌚", 190, "smartwatch", 299, 3999,
            ["{brand} Watch {#3-9}{suffix} {feature}智能手表"],
            new()
            {
                ["brand"] = DigitalBrands,
                ["suffix"] = ["", " Pro", " Lite", " Ultra"],
                ["feature"] = ["运动", "健康监测", "长续航", "eSIM通话"]
            },
            [new("尺寸", 200, "42mm", "46mm"), new("表带", 150, "氟橡胶表带", "编织表带", "金属表带")],
            ["支持血氧、心率、睡眠全天候监测", "100+ 运动模式，双频 GPS 精准定位", "典型使用 14 天续航", "蓝宝石镜面，5ATM 防水"]),

        new("CHARGER", "充电器", 2, "🔌", 45, "phone charger", 29, 399,
            ["{brand} {power} {feature}充电器"],
            new()
            {
                ["brand"] = DigitalBrands,
                ["power"] = ["20W", "33W", "65W", "100W", "140W"],
                ["feature"] = ["氮化镓快充", "多口快充", "折叠插脚", "PD快充"]
            },
            [new("规格", 20, "单口", "双口", "三口"), new("颜色", 0, "白色", "黑色")],
            ["氮化镓芯片，体积小一半", "兼容 PD/QC 多协议，笔记本手机都能充", "智能温控，多重安全防护", "折叠插脚，出差不占地方"]),

        new("POWERBANK", "移动电源", 1.5, "🔋", 120, "power bank", 59, 499,
            ["{brand} {capacity} {feature}移动电源"],
            new()
            {
                ["brand"] = DigitalBrands,
                ["capacity"] = ["10000mAh", "20000mAh", "30000mAh"],
                ["feature"] = ["自带线", "磁吸无线", "22.5W快充", "户外大容量"]
            },
            [new("颜色", 0, "白色", "黑色", "雾蓝", "浅粉")],
            ["自带双线，出门不用再带数据线", "磁吸无线充，贴上就能充", "电量数显，剩余多少一目了然", "符合民航登机规定"]),

        new("KEYBOARD", "键盘", 1.5, "⌨️", 240, "mechanical keyboard", 99, 1299,
            ["{brand} {series}{#60-108} {feature}键盘"],
            new()
            {
                ["brand"] = DigitalBrands,
                ["series"] = ["K", "KB", "MK"],
                ["feature"] = ["机械", "三模无线机械", "静音", "客制化Gasket"]
            },
            [new("轴体", 0, "红轴", "茶轴", "青轴", "银轴"), new("配色", 30, "黑色", "奶茶", "冰川蓝")],
            ["热插拔轴座，换轴不用焊", "Gasket 结构，手感柔和有回弹", "蓝牙 / 2.4G / 有线三模连接", "PBT 双色注塑键帽，久用不打油"]),

        new("MOUSE", "鼠标", 1.5, "🖱️", 250, "computer mouse", 49, 799,
            ["{brand} {series}{#1-9} {feature}鼠标"],
            new()
            {
                ["brand"] = DigitalBrands,
                ["series"] = ["M", "G", "Pro ", "Lite "],
                ["feature"] = ["无线", "游戏", "静音", "人体工学", "轻量化"]
            },
            [new("颜色", 0, "黑色", "白色", "灰粉")],
            ["26000DPI 光学传感器", "仅重 58g，长时间握持不累", "静音微动，深夜办公不吵人", "一次充电用 70 天"]),

        new("TV", "电视", 2, "📺", 230, "television living room", 1499, 19999,
            ["{brand} {size} {panel}4K {feature}电视"],
            new()
            {
                ["brand"] = ApplianceBrands,
                ["size"] = ["55英寸", "65英寸", "75英寸", "85英寸", "100英寸"],
                ["panel"] = ["Mini LED ", "QLED ", "OLED ", ""],
                ["feature"] = ["智能", "游戏", "全面屏"]
            },
            [new("安装", 199, "标准底座", "挂架安装")],
            ["144Hz 高刷，游戏不拖影", "1000+ 分区背光，暗场细节更丰富", "远场语音，说一句就能找片", "HDMI 2.1 全接口"]),

        new("PURIFIER", "空气净化器", 1, "🌬️", 170, "air purifier", 599, 3999,
            ["{brand} {series}{#3-9} {feature}空气净化器"],
            new()
            {
                ["brand"] = ApplianceBrands,
                ["series"] = ["AP", "Pure ", "Fresh "],
                ["feature"] = ["除甲醛", "除菌", "宠物家庭", "大空间"]
            },
            [new("版本", 300, "标准版", "Pro版")],
            ["CADR 600m³/h，30 分钟净化一屋", "H13 级 HEPA 滤网", "实时空气质量数显", "睡眠模式低至 22dB"]),

        new("VACUUM", "清洁电器", 1.5, "🧹", 160, "vacuum cleaner", 399, 4999,
            ["{brand} {series}{#3-12} {feature}"],
            new()
            {
                ["brand"] = ApplianceBrands,
                ["series"] = ["V", "S", "Clean "],
                ["feature"] = ["无线吸尘器", "扫地机器人", "洗地机", "除螨仪"]
            },
            [new("版本", 500, "标准版", "旗舰版")],
            ["25000Pa 大吸力，地毯缝隙也能吸干净", "激光导航，自动规划清扫路线", "自清洁烘干，告别脏抹布", "整机可水洗，滤网可拆"]),

        new("RICECOOKER", "电饭煲", 1.5, "🍚", 25, "rice cooker", 199, 1999,
            ["{brand} {capacity} {feature}电饭煲"],
            new()
            {
                ["brand"] = ApplianceBrands,
                ["capacity"] = ["3L", "4L", "5L"],
                ["feature"] = ["IH", "柴火饭", "低糖", "智能预约"]
            },
            [new("颜色", 0, "白色", "米色", "曜石黑")],
            ["IH 电磁加热，米饭受热更均匀", "球釜内胆，不粘易清洗", "24 小时预约，回家就有热饭", "多种烹饪模式，煮粥煲汤都行"]),

        new("COFFEEMAKER", "咖啡机", 1, "☕", 30, "espresso machine", 399, 6999,
            ["{brand} {feature}咖啡机 {series}{#1-9}"],
            new()
            {
                ["brand"] = ApplianceBrands,
                ["series"] = ["CM", "Barista ", "Caffè "],
                ["feature"] = ["意式半自动", "全自动", "胶囊", "美式滴滤"]
            },
            [new("颜色", 0, "银色", "奶白", "复古绿")],
            ["20Bar 泵压，萃取油脂丰富", "蒸汽打奶，在家做拿铁", "一键清洗，维护省心", "小巧机身，厨房不占地方"]),

        new("TSHIRT", "T恤", 5, "👕", 195, "t-shirt", 39, 299,
            ["{brand} {season}{material}{fit}{collar}T恤 {gender}"],
            new()
            {
                ["brand"] = ApparelBrands,
                ["season"] = ["夏季", "春夏", ""],
                ["material"] = ["纯棉", "新疆长绒棉", "冰丝", "重磅", "速干"],
                ["fit"] = ["宽松", "修身", "落肩"],
                ["collar"] = ["圆领", "V领", ""],
                ["gender"] = Genders
            },
            [new("颜色", 0, ClothingColors), new("尺码", 0, ClothingSizes)],
            ["面料柔软亲肤，透气不闷汗", "预缩处理，洗后不易变形", "领口罗纹加固，不易松垮", "百搭基础款，单穿内搭都合适"]),

        new("DRESS", "连衣裙", 3, "👗", 330, "dress fashion", 99, 899,
            ["{brand} {season}{material}{style}连衣裙"],
            new()
            {
                ["brand"] = ApparelBrands,
                ["season"] = ["春季", "夏季", "秋季", ""],
                ["material"] = ["雪纺", "真丝", "棉麻", "针织", "醋酸"],
                ["style"] = ["法式", "碎花", "收腰", "A字", "长款"]
            },
            [new("颜色", 0, "杏色", "黑色", "碎花蓝", "奶白", "雾粉"), new("尺码", 0, "S", "M", "L", "XL")],
            ["垂坠面料，行走间更显飘逸", "收腰设计，修饰身形", "可调节肩带，贴合不同身材", "内衬加厚，不透不闷"]),

        new("HOODIE", "卫衣", 2.5, "🧥", 20, "hoodie", 89, 599,
            ["{brand} {material}{style}{collar}卫衣 {gender}"],
            new()
            {
                ["brand"] = ApparelBrands,
                ["material"] = ["加绒", "重磅纯棉", "华夫格", ""],
                ["style"] = ["宽松", "落肩", "印花", "复古"],
                ["collar"] = ["连帽", "圆领"],
                ["gender"] = Genders
            },
            [new("颜色", 0, ClothingColors), new("尺码", 0, ClothingSizes)],
            ["380g 重磅面料，挺括有型", "内里磨毛，秋冬穿更暖和", "袖口下摆罗纹收口，防风保暖", "宽松版型，日常通勤百搭"]),

        new("JEANS", "牛仔裤", 2.5, "👖", 215, "jeans denim", 99, 699,
            ["{brand} {rise}{fit}{feature}牛仔裤 {gender}"],
            new()
            {
                ["brand"] = ApparelBrands,
                ["rise"] = ["高腰", "中腰", ""],
                ["fit"] = ["直筒", "修身", "阔腿", "锥形", "微喇"],
                ["feature"] = ["弹力", "水洗", "复古", ""],
                ["gender"] = ["男款", "女款"]
            },
            [new("颜色", 0, "浅蓝", "深蓝", "黑色", "烟灰"), new("尺码", 0, "26", "27", "28", "29", "30", "31", "32", "34")],
            ["含 2% 氨纶，蹲坐不紧绷", "石磨水洗工艺，颜色自然", "九分长度，露出脚踝更显高", "金属铆钉加固，耐穿不开线"]),

        new("SNEAKER", "运动鞋", 3.5, "👟", 100, "running shoes", 199, 1499,
            ["{brand} {series} {#1-9} {feature}{noun} {gender}"],
            new()
            {
                ["brand"] = ShoeBrands,
                ["series"] = ["Flash", "Swift", "Cloud", "Rise", "Air Run"],
                ["feature"] = ["缓震", "轻量", "碳板竞速", "透气", "复古"],
                ["noun"] = ["跑鞋", "运动鞋", "板鞋", "篮球鞋"],
                ["gender"] = Genders
            },
            [new("颜色", 0, "白色", "黑白", "灰蓝", "荧光绿", "米杏"), new("尺码", 0, "36", "37", "38", "39", "40", "41", "42", "43", "44")],
            ["超临界发泡中底，回弹强劲", "飞织鞋面，轻盈透气", "耐磨橡胶大底，抓地力强", "宽楦设计，久穿不挤脚"]),

        new("BACKPACK", "双肩包", 2, "🎒", 35, "backpack", 99, 899,
            ["{brand} {feature}{use}双肩包"],
            new()
            {
                ["brand"] = BagBrands,
                ["feature"] = ["防泼水", "大容量", "轻便", "可扩容"],
                ["use"] = ["通勤", "商务电脑", "旅行", "学生"]
            },
            [new("颜色", 0, "黑色", "深灰", "藏青", "卡其"), new("规格", 50, "15.6英寸", "17英寸")],
            ["独立电脑仓，防震防磕", "YKK 拉链，顺滑耐用", "背部透气网布，夏天不闷", "隐藏防盗口袋，证件更安全"]),

        new("LUGGAGE", "行李箱", 1.5, "🧳", 10, "suitcase travel", 299, 2499,
            ["{brand} {material}{feature}行李箱"],
            new()
            {
                ["brand"] = BagBrands,
                ["material"] = ["PC", "铝框", "铝镁合金"],
                ["feature"] = ["静音万向轮", "拉链款", "前开口", "超轻"]
            },
            [new("尺寸", 100, "20英寸", "24英寸", "28英寸"), new("颜色", 0, "银色", "黑色", "雾蓝", "奶白")],
            ["TSA 海关锁，出境更方便", "静音万向轮，360° 顺滑", "内置分区隔层，收纳井井有条", "20 英寸可登机"]),

        new("SKINCARE", "护肤品", 2.5, "🧴", 340, "skincare cream", 89, 899,
            ["{brand} {ingredient}{effect}{noun}"],
            new()
            {
                ["brand"] = BeautyBrands,
                ["ingredient"] = ["玻色因", "烟酰胺", "神经酰胺", "积雪草", "透明质酸"],
                ["effect"] = ["修护", "保湿", "紧致", "舒缓", "提亮"],
                ["noun"] = ["面霜", "精华液", "乳液", "爽肤水"]
            },
            [new("规格", 80, "30ml", "50ml", "100ml")],
            ["温和配方，敏感肌可用", "质地轻薄好吸收，不黏腻", "经第三方人体功效测试", "不添加酒精与香精"]),

        new("LIPSTICK", "彩妆", 1.5, "💄", 350, "lipstick makeup", 59, 399,
            ["{brand} {texture}{noun}"],
            new()
            {
                ["brand"] = BeautyBrands,
                ["texture"] = ["丝绒哑光", "水光", "镜面", "滋润", "雾面"],
                ["noun"] = ["口红", "唇釉", "唇泥"]
            },
            [new("色号", 0, "#01 正红", "#02 豆沙", "#03 枫叶", "#04 番茄", "#05 奶茶", "#06 玫瑰")],
            ["高显色，一抹上色", "持久不易脱妆", "添加植物油脂，不拔干", "磁吸管身，开合有质感"]),

        new("SHAMPOO", "洗发护发", 2, "🧼", 160, "shampoo bottle", 29, 199,
            ["{brand} {ingredient}{effect}{noun}"],
            new()
            {
                ["brand"] = BeautyBrands,
                ["ingredient"] = ["氨基酸", "生姜", "茶树", "无硅油"],
                ["effect"] = ["控油", "去屑", "蓬松", "修护"],
                ["noun"] = ["洗发水", "护发素", "洗护套装"]
            },
            [new("规格", 20, "400ml", "750ml", "1L")],
            ["氨基酸表活，温和清洁", "控油蓬松，48 小时不塌", "淡淡植物香，清新不刺鼻", "大容量家庭装更划算"]),

        new("TOOTHBRUSH", "电动牙刷", 1, "🪥", 185, "electric toothbrush", 99, 899,
            ["{brand} {series}{#1-9} {feature}电动牙刷"],
            new()
            {
                ["brand"] = BeautyBrands,
                ["series"] = ["T", "Sonic ", "Pro "],
                ["feature"] = ["声波", "智能压力感应", "便携"]
            },
            [new("颜色", 0, "白色", "黑色", "雾粉", "薄荷绿")],
            ["每分钟 40000 次声波振动", "压力过大自动提醒，保护牙龈", "一次充电用 60 天", "IPX7 防水，可全身水洗"]),

        new("COFFEEBEAN", "咖啡豆", 1.5, "🫘", 30, "coffee beans", 39, 299,
            ["{brand} {origin}{roast}咖啡豆"],
            new()
            {
                ["brand"] = FoodBrands,
                ["origin"] = ["云南保山", "埃塞俄比亚耶加雪菲", "哥伦比亚", "巴西", "危地马拉"],
                ["roast"] = ["浅烘", "中烘", "深烘", "意式拼配"]
            },
            [new("规格", 40, "250g", "500g", "1kg"), new("研磨", 0, "咖啡豆", "手冲粉", "意式粉")],
            ["当季新鲜烘焙，烘焙后 7 天内发货", "单向排气阀包装，锁住香气", "风味：坚果、焦糖、柑橘", "适合手冲、意式与冷萃"]),

        new("TEA", "茶叶", 2, "🍵", 95, "tea leaves", 49, 999,
            ["{brand} {year}{grade}{tea}"],
            new()
            {
                ["brand"] = TeaBrands,
                ["year"] = ["2026年", "2025年", ""],
                ["grade"] = ["特级", "一级", ""],
                ["tea"] = ["西湖龙井", "武夷山大红袍", "安溪铁观音", "云南普洱", "黄山毛峰", "福鼎白茶", "祁门红茶", "信阳毛尖"]
            },
            [new("规格", 60, "100g", "250g", "500g"), new("包装", 80, "罐装", "礼盒装")],
            ["核心产区原料，产地直采", "传统工艺制作，回甘明显", "独立小包装，冲泡方便", "送礼自饮两相宜"]),

        new("NUTS", "坚果零食", 2, "🥜", 35, "mixed nuts snack", 29, 199,
            ["{brand} {flavor}{nut}"],
            new()
            {
                ["brand"] = FoodBrands,
                ["flavor"] = ["每日", "原味", "盐焗", "奶油", ""],
                ["nut"] = ["坚果礼盒", "夏威夷果", "碧根果", "巴旦木", "开心果", "腰果", "混合坚果"]
            },
            [new("规格", 20, "250g", "500g", "1kg")],
            ["颗颗饱满，当季新货", "低温烘焙，保留坚果原香", "独立小包，开袋即食", "不添加防腐剂"]),

        new("BEDDING", "床品", 2, "🛏️", 205, "bedding set bedroom", 199, 1999,
            ["{brand} {material}{style}{noun}"],
            new()
            {
                ["brand"] = HomeBrands,
                ["material"] = ["100支长绒棉", "天丝", "全棉磨毛", "水洗棉", "真丝"],
                ["style"] = ["纯色", "简约条纹", "小碎花", "北欧"],
                ["noun"] = ["四件套", "被套", "床笠三件套"]
            },
            [new("尺寸", 100, "1.5m床", "1.8m床", "2.0m床"), new("颜色", 0, "米白", "雾蓝", "浅灰", "豆沙粉")],
            ["A 类婴幼儿标准，贴身睡更安心", "活性印染，多次水洗不褪色", "隐形拉链，换洗方便", "亲肤柔软，越洗越软"]),

        new("STORAGE", "收纳用品", 1.5, "📦", 45, "storage box organizer", 19, 199,
            ["{brand} {material}{feature}{noun}"],
            new()
            {
                ["brand"] = HomeBrands,
                ["material"] = ["透明PP", "布艺", "竹木", "亚克力"],
                ["feature"] = ["可叠加", "带盖", "抽屉式", "折叠"],
                ["noun"] = ["收纳盒", "收纳箱", "收纳柜"]
            },
            [new("规格", 15, "小号", "中号", "大号")],
            ["食品级材质，无异味", "可叠放设计，节省空间", "透明可视，找东西一眼看到", "加厚承重，不易变形"]),

        new("LAMP", "台灯", 1, "💡", 50, "desk lamp", 69, 899,
            ["{brand} {series}{#1-9} {feature}台灯"],
            new()
            {
                ["brand"] = HomeBrands,
                ["series"] = ["L", "Lumi ", "Read "],
                ["feature"] = ["国AA级护眼", "全光谱", "无线充电", "智能感光", "学习"]
            },
            [new("颜色", 0, "白色", "黑色", "原木色")],
            ["无可视频闪，长时间阅读不累眼", "Ra97 高显色，还原真实色彩", "无极调光调色", "折叠灯臂，角度随心调"]),

        new("YOGA", "瑜伽垫", 1, "🧘", 140, "yoga mat", 49, 399,
            ["{brand} {material}{thickness}{feature}瑜伽垫"],
            new()
            {
                ["brand"] = OutdoorBrands,
                ["material"] = ["TPE", "天然橡胶", "NBR", "PU"],
                ["thickness"] = ["6mm", "8mm", "10mm", ""],
                ["feature"] = ["防滑", "加宽", "双面"]
            },
            [new("颜色", 0, "雾紫", "豆绿", "藏青", "樱粉")],
            ["双面防滑纹理，出汗也不打滑", "高回弹，保护膝盖", "附赠绑带，携带方便", "无异味环保材质"]),

        new("TENT", "户外露营", 1, "⛺", 110, "camping tent", 199, 2999,
            ["{brand} {people}{feature}帐篷", "{brand} {canopy}天幕"],
            new()
            {
                ["brand"] = OutdoorBrands,
                ["people"] = ["单人", "双人", "3-4人", "5-8人"],
                ["feature"] = ["速开", "防暴雨", "露营", "轻量化"],
                ["canopy"] = ["防晒", "黑胶", "蝶形", "加厚银胶"]
            },
            [new("颜色", 0, "军绿", "沙色", "橙色")],
            ["3000mm 防水指数，雨天无忧", "一体式速开，60 秒搭好", "双层通风设计，夏天不闷", "附送地钉与收纳袋"]),

        new("DIAPER", "母婴用品", 1.5, "🍼", 30, "baby diapers", 79, 299,
            ["{brand} {series}{feature}{noun}"],
            new()
            {
                ["brand"] = ["小树", "贝壳宝贝", "暖暖", "柔芽"],
                ["series"] = ["柔芯", "云感", "轻薄", ""],
                ["feature"] = ["超薄透气", "夜用", ""],
                ["noun"] = ["纸尿裤", "拉拉裤"]
            },
            [new("尺码", 0, "NB", "S", "M", "L", "XL"), new("规格", 40, "单包", "箱装")],
            ["3D 立体护围，防侧漏", "瞬吸干爽，一夜安睡", "弱酸亲肤表层，呵护娇嫩肌肤", "尿显提醒，换片更及时"]),

        new("PETFOOD", "宠物食品", 2, "🐾", 25, "pet food cat dog", 49, 499,
            ["{brand} {feature}{noun}"],
            new()
            {
                ["brand"] = ["毛孩子", "喵汪", "暖爪", "鲜宠"],
                ["feature"] = ["无谷", "冻干双拼", "鲜肉", "低敏"],
                ["noun"] = ["幼猫猫粮", "成猫猫粮", "全阶段猫粮", "幼犬狗粮", "小型犬成犬粮", "中大型犬成犬粮"]
            },
            [new("规格", 60, "1.5kg", "4kg", "10kg")],
            ["粗蛋白 ≥40%，鲜肉为第一原料", "添加益生菌，呵护肠胃", "颗粒大小适口，挑食也爱吃", "独立分装，开袋保鲜"])
    ];
}
