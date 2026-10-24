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
    private static readonly string[] DigitalBrands = ["星澜", "极光", "曜石", "北辰", "青柠", "遥光", "凌越", "木星", "云帆", "启明", "知物", "锐腾"];
    private static readonly string[] AudioBrands = ["声阔", "律动", "回响", "听澜", "静屿", "弦音", "清音", "声屿", "乐动", "悦耳"];
    private static readonly string[] ApplianceBrands = ["美家", "暖居", "净界", "小厨", "安住", "悦享", "禾田", "家悦", "清朗", "智厨", "优宅", "温客"];
    private static readonly string[] ApparelBrands = ["木禾", "素衫", "城野", "拾光", "青衿", "布语", "野森", "初棉", "棉觉", "衣简", "南岸", "知秋", "白鹭", "森屿"];
    private static readonly string[] ShoeBrands = ["疾风", "跃动", "行者", "轻踏", "奔途", "迅驰", "飞驰", "步履", "跃界", "疾行"];
    private static readonly string[] BagBrands = ["途乐", "行囊", "拾光", "城野", "远行客", "旅人", "背客", "简行", "山岚", "途远"];
    private static readonly string[] BeautyBrands = ["花研", "植萃集", "澄肌", "柔沐", "研色", "初颜", "肌源", "水漾", "花间集", "素研", "沐光", "雪颜"];
    private static readonly string[] FoodBrands = ["山野食记", "果园派", "晨烘", "坚果日记", "鲜语", "田园记", "果然鲜", "麦香村", "舌尖派", "原野"];
    private static readonly string[] TeaBrands = ["茶语", "山野食记", "一叶", "云雾间", "茗香阁", "春山", "竹溪", "清露"];
    private static readonly string[] HomeBrands = ["栖居", "素居", "简屋", "棉朵", "暖居", "居物", "木语", "宜室", "暖棉", "舒眠"];
    private static readonly string[] OutdoorBrands = ["野径", "远山", "风行者", "极地", "山行", "旷野", "林间", "越岭", "野趣", "途岳"];
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
                ["series"] = ["Nova", "Air", "Edge", "Vista", "Neo", "Spark"],
                ["suffix"] = ["", " Pro", " Plus", " Mini", " Ultra"],
                ["feature"] = ["", "11英寸 ", "12.4英寸 ", "2.8K高刷", "护眼", "学生", "办公"]
            },
            [new("颜色", 0, "深空灰", "银色", "薄荷绿", "星光白"), new("存储", 400, "8GB+128GB", "8GB+256GB", "12GB+512GB"), new("网络", 500, "WLAN版", "5G版")],
            ["支持手写笔与键盘，学习办公两不误", "四扬声器环绕立体声，追剧更沉浸", "轻至 480g，单手握持不吃力", "平行视界多窗口，效率翻倍"]),

        new("LAPTOP", "笔记本电脑", 3, "💻", 205, "laptop", 3499, 14999,
            ["{brand} {series} {year} {cpu}{size}{feature}笔记本电脑"],
            new()
            {
                ["brand"] = DigitalBrands,
                ["series"] = ["Book", "Book Pro", "Station", "Air", "Titan", "Zen", "Flex", "Nova Book"],
                ["year"] = ["2024款", "2025款", "2026款"],
                ["cpu"] = ["酷睿Ultra ", "锐龙AI ", ""],
                ["size"] = ["14英寸", "15.6英寸", "16英寸", "13.3英寸", "17.3英寸"],
                ["feature"] = ["轻薄", "高性能", "游戏", "商务", "创作", "学生", "设计师", "AI"]
            },
            [new("配置", 800, "i5/16G/512G", "i7/16G/1T", "i7/32G/1T", "i9/32G/2T"), new("颜色", 0, "银色", "深空灰", "雾蓝")],
            ["2.8K OLED 屏，100% DCI-P3 广色域", "全金属机身，厚度仅 14.9mm", "满血独显，3A 大作流畅运行", "长效续航 14 小时，出差不带充电器"]),

        new("MONITOR", "显示器", 1.5, "🖥️", 200, "computer monitor", 699, 4999,
            ["{brand} {size} {res} {refresh}{panel}{feature}显示器"],
            new()
            {
                ["brand"] = DigitalBrands,
                ["size"] = ["24英寸", "25英寸", "27英寸", "32英寸", "34英寸带鱼屏", "49英寸带鱼屏"],
                ["res"] = ["2K", "4K", "5K", "1080P"],
                ["refresh"] = ["144Hz ", "165Hz ", "240Hz ", ""],
                ["panel"] = ["IPS", "Fast IPS", "OLED", "VA"],
                ["feature"] = ["电竞", "设计师", "护眼", ""]
            },
            [new("支架", 100, "标准支架", "升降旋转支架")],
            ["165Hz 高刷新率，1ms 响应", "出厂逐台校色，ΔE<2", "Type-C 一线连接，65W 反向供电", "硬件级低蓝光，莱茵认证"]),

        new("HEADPHONE", "耳机", 4, "🎧", 270, "headphones", 99, 2999,
            ["{brand} {series} {#2-9}{suffix} {feature}{noun}"],
            new()
            {
                ["brand"] = AudioBrands,
                ["series"] = ["Buds", "Free", "Echo", "Pulse", "Studio", "Air", "Sound", "Neo"],
                ["suffix"] = ["", " Pro", " Lite", " Max"],
                ["feature"] = ["主动降噪", "真无线", "头戴式", "开放式", "运动", "电竞", "骨传导", "半入耳式", "挂耳式"],
                ["noun"] = ["蓝牙耳机", "耳机"]
            },
            [new("颜色", 0, "月光白", "石墨黑", "雾霾蓝", "樱花粉")],
            ["深度降噪 50dB，通勤路上一秒安静", "LDAC 高清音质，细节清晰", "单次续航 8 小时，配合充电盒 36 小时", "双设备同时连接，手机电脑无缝切换"]),

        new("SPEAKER", "蓝牙音箱", 1, "🔊", 280, "bluetooth speaker", 129, 2499,
            ["{brand} {series} {#1-9}{suffix} {feature}蓝牙音箱"],
            new()
            {
                ["brand"] = AudioBrands,
                ["series"] = ["Boom", "Go", "Home", "Party", "Mini", "Wave"],
                ["suffix"] = ["", " Pro", " Max"],
                ["feature"] = ["便携", "IPX7防水", "桌面", "重低音", "智能", "户外", "车载"]
            },
            [new("颜色", 0, "石墨黑", "月光白", "森林绿", "珊瑚红")],
            ["360° 环绕声场，派对一台就够", "IPX7 级防水，户外随身带", "两台互联组成立体声", "20 小时超长续航"]),

        new("WATCH", "智能手表", 2, "⌚", 190, "smartwatch", 299, 3999,
            ["{brand} {series} {#3-9}{suffix} {feature}{noun}"],
            new()
            {
                ["brand"] = DigitalBrands,
                ["series"] = ["Watch", "Band", "Fit"],
                ["suffix"] = ["", " Pro", " Lite", " Ultra", " Max", " SE"],
                ["feature"] = ["运动", "健康监测", "长续航", "eSIM通话", "血压监测", "户外", "商务"],
                ["noun"] = ["智能手表", "运动手表"]
            },
            [new("尺寸", 200, "42mm", "46mm"), new("表带", 150, "氟橡胶表带", "编织表带", "金属表带")],
            ["支持血氧、心率、睡眠全天候监测", "100+ 运动模式，双频 GPS 精准定位", "典型使用 14 天续航", "蓝宝石镜面，5ATM 防水"]),

        new("CHARGER", "充电器", 2, "🔌", 45, "phone charger", 29, 399,
            ["{brand} {power}{port}{feature}{noun}", "{brand} {length}{wire}{cable}数据线"],
            new()
            {
                ["brand"] = DigitalBrands,
                ["power"] = ["20W", "25W", "33W", "45W", "65W", "67W", "100W", "120W", "140W"],
                ["port"] = ["单口", "双口", "三口", "四口", ""],
                ["feature"] = ["氮化镓快充", "多口快充", "折叠插脚", "PD快充", "超薄", "迷你", "磁吸", "桌面", "智能温控"],
                ["noun"] = ["充电器", "充电头", "快充套装", "多口充电站"],
                ["length"] = ["0.5m ", "1m ", "1.5m ", "2m "],
                ["wire"] = ["编织", "液态硅胶", "弯头", ""],
                ["cable"] = ["Type-C to C 100W", "USB-A to C 66W", "Lightning 27W", "三合一", "磁吸"]
            },
            [new("规格", 20, "单口", "双口", "三口"), new("颜色", 0, "白色", "黑色")],
            ["氮化镓芯片，体积小一半", "兼容 PD/QC 多协议，笔记本手机都能充", "智能温控，多重安全防护", "折叠插脚，出差不占地方"]),

        new("POWERBANK", "移动电源", 1.5, "🔋", 120, "power bank", 59, 499,
            ["{brand} {capacity} {power}{scene}{feature}{noun}"],
            new()
            {
                ["brand"] = DigitalBrands,
                ["capacity"] = ["5000mAh", "10000mAh", "20000mAh", "25000mAh", "30000mAh"],
                ["power"] = ["22.5W", "30W", "65W", "100W", ""],
                ["scene"] = ["", "便携", "旅行", "户外", "学生"],
                ["feature"] = ["自带线", "磁吸无线", "大容量", "迷你", "超薄", "数显", "笔记本", "双向快充"],
                ["noun"] = ["移动电源", "充电宝"]
            },
            [new("颜色", 0, "白色", "黑色", "雾蓝", "浅粉")],
            ["自带双线，出门不用再带数据线", "磁吸无线充，贴上就能充", "电量数显，剩余多少一目了然", "符合民航登机规定"]),

        new("KEYBOARD", "键盘", 1.5, "⌨️", 240, "mechanical keyboard", 99, 1299,
            ["{brand} {series}{#60-108} {feature}键盘"],
            new()
            {
                ["brand"] = DigitalBrands,
                ["series"] = ["K", "KB", "MK", "GK"],
                ["feature"] = ["机械", "三模无线机械", "静音", "客制化Gasket", "矮轴", "电竞", "办公"]
            },
            [new("轴体", 0, "红轴", "茶轴", "青轴", "银轴"), new("配色", 30, "黑色", "奶茶", "冰川蓝")],
            ["热插拔轴座，换轴不用焊", "Gasket 结构，手感柔和有回弹", "蓝牙 / 2.4G / 有线三模连接", "PBT 双色注塑键帽，久用不打油"]),

        new("MOUSE", "鼠标", 1.5, "🖱️", 250, "computer mouse", 49, 799,
            ["{brand} {series}{#1-9} {dpi}{feature}鼠标"],
            new()
            {
                ["brand"] = DigitalBrands,
                ["series"] = ["M", "G", "X", "Pro ", "Lite ", "Air "],
                ["dpi"] = ["", "26K ", "8K回报率 "],
                ["feature"] = ["无线", "游戏", "静音", "人体工学", "轻量化", "蓝牙双模", "垂直", "办公"]
            },
            [new("颜色", 0, "黑色", "白色", "灰粉")],
            ["26000DPI 光学传感器", "仅重 58g，长时间握持不累", "静音微动，深夜办公不吵人", "一次充电用 70 天"]),

        new("TV", "电视", 2, "📺", 230, "television living room", 1499, 19999,
            ["{brand} {size} {panel}4K {refresh}{feature}{noun}"],
            new()
            {
                ["brand"] = ApplianceBrands,
                ["size"] = ["43英寸", "50英寸", "55英寸", "65英寸", "75英寸", "85英寸", "100英寸"],
                ["panel"] = ["Mini LED ", "QLED ", "OLED ", "ULED ", ""],
                ["refresh"] = ["", "120Hz ", "144Hz ", "240Hz "],
                ["feature"] = ["智能", "游戏", "全面屏", "护眼", "AI", "艺术"],
                ["noun"] = ["电视", "平板电视"]
            },
            [new("安装", 199, "标准底座", "挂架安装")],
            ["144Hz 高刷，游戏不拖影", "1000+ 分区背光，暗场细节更丰富", "远场语音，说一句就能找片", "HDMI 2.1 全接口"]),

        new("PURIFIER", "空气净化器", 1, "🌬️", 170, "air purifier", 599, 3999,
            ["{brand} {series}{#3-9}{suffix} {feature}{noun}"],
            new()
            {
                ["brand"] = ApplianceBrands,
                ["series"] = ["AP", "Pure ", "Fresh ", "Air ", "Clean "],
                ["suffix"] = ["", " Pro", " Max"],
                ["feature"] = ["除甲醛", "除菌", "宠物家庭", "大空间", "除异味", "母婴", "静音", "智能"],
                ["noun"] = ["空气净化器", "新风净化器"]
            },
            [new("版本", 300, "标准版", "Pro版")],
            ["CADR 600m³/h，30 分钟净化一屋", "H13 级 HEPA 滤网", "实时空气质量数显", "睡眠模式低至 22dB"]),

        new("VACUUM", "清洁电器", 1.5, "🧹", 160, "vacuum cleaner", 399, 4999,
            ["{brand} {series}{#3-12}{suffix} {feature}"],
            new()
            {
                ["brand"] = ApplianceBrands,
                ["series"] = ["V", "S", "Clean ", "Max ", "Home "],
                ["suffix"] = ["", " Pro", " Plus"],
                ["feature"] = ["无线吸尘器", "扫地机器人", "洗地机", "除螨仪", "手持吸尘器", "擦窗机器人", "蒸汽拖把", "布艺清洗机"]
            },
            [new("版本", 500, "标准版", "旗舰版")],
            ["25000Pa 大吸力，地毯缝隙也能吸干净", "激光导航，自动规划清扫路线", "自清洁烘干，告别脏抹布", "整机可水洗，滤网可拆"]),

        new("RICECOOKER", "电饭煲", 1.5, "🍚", 25, "rice cooker", 199, 1999,
            ["{brand} {style}{feature}{noun} {capacity} {people}"],
            new()
            {
                ["brand"] = ApplianceBrands,
                ["style"] = ["", "复古", "奶油风", "小巧"],
                ["feature"] = ["IH", "柴火饭", "低糖", "智能预约", "迷你", "多功能", "陶瓷内胆", "压力"],
                ["noun"] = ["电饭煲", "电饭锅"],
                ["capacity"] = ["1.6L", "2L", "3L", "4L", "5L", "6L"],
                ["people"] = ["", "1-2人", "2-4人", "3-6人", "家用"]
            },
            [new("颜色", 0, "白色", "米色", "曜石黑")],
            ["IH 电磁加热，米饭受热更均匀", "球釜内胆，不粘易清洗", "24 小时预约，回家就有热饭", "多种烹饪模式，煮粥煲汤都行"]),

        new("COFFEEMAKER", "咖啡机", 1, "☕", 30, "espresso machine", 399, 6999,
            ["{brand} {scene}{feature}咖啡机 {series}{#1-9}"],
            new()
            {
                ["brand"] = ApplianceBrands,
                ["scene"] = ["家用", "办公室", "小型", ""],
                ["series"] = ["CM", "Barista ", "Caffè ", "Espresso ", "Brew "],
                ["feature"] = ["意式半自动", "全自动", "胶囊", "美式滴滤", "冷萃", "手冲", "研磨一体"]
            },
            [new("颜色", 0, "银色", "奶白", "复古绿")],
            ["20Bar 泵压，萃取油脂丰富", "蒸汽打奶，在家做拿铁", "一键清洗，维护省心", "小巧机身，厨房不占地方"]),

        new("TSHIRT", "T恤", 5, "👕", 195, "t-shirt", 39, 299,
            ["{brand} {season}{material}{style}{fit}{collar}T恤 {gender}"],
            new()
            {
                ["brand"] = ApparelBrands,
                ["season"] = ["夏季", "春夏", ""],
                ["material"] = ["纯棉", "新疆长绒棉", "冰丝", "重磅", "速干", "莫代尔", "华夫格"],
                ["style"] = ["", "简约", "印花", "刺绣", "字母", "条纹", "撞色", "复古", "日系", "美式"],
                ["fit"] = ["宽松", "修身", "落肩"],
                ["collar"] = ["圆领", "V领", ""],
                ["gender"] = Genders
            },
            [new("颜色", 0, ClothingColors), new("尺码", 0, ClothingSizes)],
            ["面料柔软亲肤，透气不闷汗", "预缩处理，洗后不易变形", "领口罗纹加固，不易松垮", "百搭基础款，单穿内搭都合适"]),

        new("DRESS", "连衣裙", 3, "👗", 330, "dress fashion", 99, 899,
            ["{brand} {season}{occasion}{material}{style}{length}连衣裙"],
            new()
            {
                ["brand"] = ApparelBrands,
                ["season"] = ["春季", "夏季", "秋季", ""],
                ["occasion"] = ["", "度假", "通勤", "气质"],
                ["material"] = ["雪纺", "真丝", "棉麻", "针织", "醋酸", "天丝", "蕾丝"],
                ["style"] = ["法式", "碎花", "收腰", "A字", "吊带", "衬衫式", "泡泡袖", "方领"],
                ["length"] = ["", "中长款", "短款", "长款"]
            },
            [new("颜色", 0, "杏色", "黑色", "碎花蓝", "奶白", "雾粉"), new("尺码", 0, "S", "M", "L", "XL")],
            ["垂坠面料，行走间更显飘逸", "收腰设计，修饰身形", "可调节肩带，贴合不同身材", "内衬加厚，不透不闷"]),

        new("HOODIE", "卫衣", 2.5, "🧥", 20, "hoodie", 89, 599,
            ["{brand} {season}{fit}{material}{style}{collar}卫衣 {gender}"],
            new()
            {
                ["brand"] = ApparelBrands,
                ["season"] = ["", "秋冬", "春秋"],
                ["fit"] = ["", "宽松", "oversize"],
                ["material"] = ["加绒", "重磅纯棉", "华夫格", "空气层", "摇粒绒", ""],
                ["style"] = ["落肩", "印花", "复古", "字母", "刺绣", "纯色", "美式", "撞色"],
                ["collar"] = ["连帽", "圆领", "半拉链"],
                ["gender"] = Genders
            },
            [new("颜色", 0, ClothingColors), new("尺码", 0, ClothingSizes)],
            ["380g 重磅面料，挺括有型", "内里磨毛，秋冬穿更暖和", "袖口下摆罗纹收口，防风保暖", "宽松版型，日常通勤百搭"]),

        new("JEANS", "牛仔裤", 2.5, "👖", 215, "jeans denim", 99, 699,
            ["{brand} {season}{rise}{fit}{feature}{length}牛仔裤 {gender}"],
            new()
            {
                ["brand"] = ApparelBrands,
                ["season"] = ["", "春秋", "夏季薄款", "冬季"],
                ["rise"] = ["高腰", "中腰", ""],
                ["fit"] = ["直筒", "修身", "阔腿", "锥形", "微喇", "烟管", "小脚"],
                ["feature"] = ["弹力", "水洗", "复古", "破洞", "毛边", "加绒", ""],
                ["length"] = ["", "九分", "长款", "八分"],
                ["gender"] = ["男款", "女款"]
            },
            [new("颜色", 0, "浅蓝", "深蓝", "黑色", "烟灰"), new("尺码", 0, "26", "27", "28", "29", "30", "31", "32", "34")],
            ["含 2% 氨纶，蹲坐不紧绷", "石磨水洗工艺，颜色自然", "九分长度，露出脚踝更显高", "金属铆钉加固，耐穿不开线"]),

        new("SNEAKER", "运动鞋", 3.5, "👟", 100, "running shoes", 199, 1499,
            ["{brand} {series} {#1-9} {feature}{noun} {gender}"],
            new()
            {
                ["brand"] = ShoeBrands,
                ["series"] = ["Flash", "Swift", "Cloud", "Rise", "Air Run", "Pace", "Storm"],
                ["feature"] = ["缓震", "轻量", "碳板竞速", "透气", "复古"],
                ["noun"] = ["跑鞋", "运动鞋", "板鞋", "篮球鞋"],
                ["gender"] = Genders
            },
            [new("颜色", 0, "白色", "黑白", "灰蓝", "荧光绿", "米杏"), new("尺码", 0, "36", "37", "38", "39", "40", "41", "42", "43", "44")],
            ["超临界发泡中底，回弹强劲", "飞织鞋面，轻盈透气", "耐磨橡胶大底，抓地力强", "宽楦设计，久穿不挤脚"]),

        new("BACKPACK", "双肩包", 2, "🎒", 35, "backpack", 99, 899,
            ["{brand} {capacity}{material}{feature}{use}{noun}"],
            new()
            {
                ["brand"] = BagBrands,
                ["capacity"] = ["", "20L", "25L", "35L"],
                ["material"] = ["", "尼龙", "牛津布", "帆布", "真皮"],
                ["feature"] = ["防泼水", "大容量", "轻便", "可扩容", "防盗", "多隔层", "超轻", "户外"],
                ["use"] = ["通勤", "商务电脑", "旅行", "学生", "登山", "休闲"],
                ["noun"] = ["双肩包", "背包"]
            },
            [new("颜色", 0, "黑色", "深灰", "藏青", "卡其"), new("规格", 50, "15.6英寸", "17英寸")],
            ["独立电脑仓，防震防磕", "YKK 拉链，顺滑耐用", "背部透气网布，夏天不闷", "隐藏防盗口袋，证件更安全"]),

        new("LUGGAGE", "行李箱", 1.5, "🧳", 10, "suitcase travel", 299, 2499,
            ["{brand} {scene}{style}{material}{feature}{noun}"],
            new()
            {
                ["brand"] = BagBrands,
                ["scene"] = ["", "商务", "学生", "男女"],
                ["style"] = ["", "复古", "简约", "撞色", "纯色"],
                ["material"] = ["PC", "铝框", "铝镁合金", "PP", "帆布"],
                ["feature"] = ["静音万向轮", "拉链款", "前开口", "超轻", "大容量", "防刮", "可扩展"],
                ["noun"] = ["行李箱", "拉杆箱", "登机箱", "旅行箱"]
            },
            [new("尺寸", 100, "20英寸", "24英寸", "28英寸"), new("颜色", 0, "银色", "黑色", "雾蓝", "奶白")],
            ["TSA 海关锁，出境更方便", "静音万向轮，360° 顺滑", "内置分区隔层，收纳井井有条", "20 英寸可登机"]),

        new("SKINCARE", "护肤品", 2.5, "🧴", 340, "skincare cream", 89, 899,
            ["{brand} {skin}{ingredient}{effect}{noun}"],
            new()
            {
                ["brand"] = BeautyBrands,
                ["skin"] = ["", "敏感肌", "干皮", "油皮"],
                ["ingredient"] = ["玻色因", "烟酰胺", "神经酰胺", "积雪草", "透明质酸", "视黄醇", "胜肽", "依克多因", "维C"],
                ["effect"] = ["修护", "保湿", "紧致", "舒缓", "提亮", "抗皱", "控油", "淡纹"],
                ["noun"] = ["面霜", "精华液", "乳液", "爽肤水", "眼霜", "面膜", "防晒霜", "洁面乳"]
            },
            [new("规格", 80, "30ml", "50ml", "100ml")],
            ["温和配方，敏感肌可用", "质地轻薄好吸收，不黏腻", "经第三方人体功效测试", "不添加酒精与香精"]),

        new("LIPSTICK", "彩妆", 1.5, "💄", 350, "lipstick makeup", 59, 399,
            ["{brand} {series}{tag}{texture}{noun}"],
            new()
            {
                ["brand"] = BeautyBrands,
                ["series"] = ["", "小金管", "小方管", "黑管", "小细管", "星空"],
                ["tag"] = ["", "不沾杯", "保湿", "显白", "持久"],
                ["texture"] = ["丝绒哑光", "水光", "镜面", "滋润", "雾面", "奶油", "磨砂", "丝缎"],
                ["noun"] = ["口红", "唇釉", "唇泥", "唇膏", "唇彩", "唇蜜"]
            },
            [new("色号", 0, "#01 正红", "#02 豆沙", "#03 枫叶", "#04 番茄", "#05 奶茶", "#06 玫瑰")],
            ["高显色，一抹上色", "持久不易脱妆", "添加植物油脂，不拔干", "磁吸管身，开合有质感"]),

        new("SHAMPOO", "洗发护发", 2, "🧼", 160, "shampoo bottle", 29, 199,
            ["{brand} {series}{ingredient}{effect}{noun} {hair}"],
            new()
            {
                ["brand"] = BeautyBrands,
                ["series"] = ["", "香氛", "专研"],
                ["ingredient"] = ["氨基酸", "生姜", "茶树", "无硅油", "海盐", "人参", "迷迭香", "玫瑰"],
                ["effect"] = ["控油", "去屑", "蓬松", "修护", "柔顺", "滋养", "防脱"],
                ["noun"] = ["洗发水", "护发素", "洗护套装", "发膜", "护发精油"],
                ["hair"] = ["", "油性发质", "干枯发质", "细软发质", "染烫受损"]
            },
            [new("规格", 20, "400ml", "750ml", "1L")],
            ["氨基酸表活，温和清洁", "控油蓬松，48 小时不塌", "淡淡植物香，清新不刺鼻", "大容量家庭装更划算"]),

        new("TOOTHBRUSH", "电动牙刷", 1, "🪥", 185, "electric toothbrush", 99, 899,
            ["{brand} {series}{#1-9}{suffix} {feature}{noun}"],
            new()
            {
                ["brand"] = BeautyBrands,
                ["series"] = ["T", "Sonic ", "Pro ", "Smile ", "Fresh "],
                ["suffix"] = ["", " Pro", " Max", " Mini"],
                ["feature"] = ["声波", "智能压力感应", "便携", "美白", "正畸", "情侣款"],
                ["noun"] = ["电动牙刷", "电动牙刷套装"]
            },
            [new("颜色", 0, "白色", "黑色", "雾粉", "薄荷绿")],
            ["每分钟 40000 次声波振动", "压力过大自动提醒，保护牙龈", "一次充电用 60 天", "IPX7 防水，可全身水洗"]),

        new("COFFEEBEAN", "咖啡豆", 1.5, "🫘", 30, "coffee beans", 39, 299,
            ["{brand} {grade}{origin}{process}{roast}{noun}"],
            new()
            {
                ["brand"] = FoodBrands,
                ["grade"] = ["", "精品", "SOE", "阿拉比卡"],
                ["origin"] = ["云南保山", "埃塞俄比亚耶加雪菲", "哥伦比亚", "巴西", "危地马拉", "肯尼亚", "印尼曼特宁", "牙买加蓝山", "巴拿马", "哥斯达黎加"],
                ["process"] = ["", "水洗", "日晒", "蜜处理", "厌氧发酵"],
                ["roast"] = ["浅烘", "中烘", "中深烘", "深烘", "意式拼配"],
                ["noun"] = ["咖啡豆", "挂耳咖啡", "冷萃咖啡粉"]
            },
            [new("规格", 40, "250g", "500g", "1kg"), new("研磨", 0, "咖啡豆", "手冲粉", "意式粉")],
            ["当季新鲜烘焙，烘焙后 7 天内发货", "单向排气阀包装，锁住香气", "风味：坚果、焦糖、柑橘", "适合手冲、意式与冷萃"]),

        new("TEA", "茶叶", 2, "🍵", 95, "tea leaves", 49, 999,
            ["{brand} {year}{season}{grade}{tea}{form}"],
            new()
            {
                ["brand"] = TeaBrands,
                ["year"] = ["2026年", "2025年", ""],
                ["season"] = ["", "春茶", "秋茶", "高山"],
                ["grade"] = ["特级", "一级", "明前", ""],
                ["tea"] = ["西湖龙井", "武夷山大红袍", "安溪铁观音", "云南普洱", "黄山毛峰", "福鼎白茶", "祁门红茶", "信阳毛尖",
                           "碧螺春", "六安瓜片", "正山小种", "滇红", "凤凰单丛", "茉莉花茶", "君山银针", "太平猴魁"],
                ["form"] = ["", "口粮茶", "送礼", "原叶"]
            },
            [new("规格", 60, "100g", "250g", "500g"), new("包装", 80, "罐装", "礼盒装")],
            ["核心产区原料，产地直采", "传统工艺制作，回甘明显", "独立小包装，冲泡方便", "送礼自饮两相宜"]),

        new("NUTS", "坚果零食", 2, "🥜", 35, "mixed nuts snack", 29, 199,
            ["{brand} {tag}{flavor}{nut}{pack}"],
            new()
            {
                ["brand"] = FoodBrands,
                ["tag"] = ["", "孕妇", "儿童", "办公室", "年货"],
                ["flavor"] = ["每日", "原味", "盐焗", "奶油", "椒盐", "蜂蜜", "炭烧", ""],
                ["nut"] = ["坚果礼盒", "夏威夷果", "碧根果", "巴旦木", "开心果", "腰果", "混合坚果", "松子", "核桃", "山核桃仁", "榛子", "每日坚果"],
                ["pack"] = ["", "罐装", "袋装", "礼盒", "量贩装"]
            },
            [new("规格", 20, "250g", "500g", "1kg")],
            ["颗颗饱满，当季新货", "低温烘焙，保留坚果原香", "独立小包，开袋即食", "不添加防腐剂"]),

        new("BEDDING", "床品", 2, "🛏️", 205, "bedding set bedroom", 199, 1999,
            ["{brand} {season}{tag}{material}{style}{noun}"],
            new()
            {
                ["brand"] = HomeBrands,
                ["season"] = ["", "四季", "夏季", "冬季加厚"],
                ["tag"] = ["", "酒店风", "简约", "轻奢"],
                ["material"] = ["100支长绒棉", "天丝", "全棉磨毛", "水洗棉", "真丝", "牛奶绒", "法兰绒"],
                ["style"] = ["纯色", "简约条纹", "小碎花", "北欧", "格子", "刺绣", "ins风"],
                ["noun"] = ["四件套", "被套", "床笠三件套", "床单", "枕套一对", "夏凉被"]
            },
            [new("尺寸", 100, "1.5m床", "1.8m床", "2.0m床"), new("颜色", 0, "米白", "雾蓝", "浅灰", "豆沙粉")],
            ["A 类婴幼儿标准，贴身睡更安心", "活性印染，多次水洗不褪色", "隐形拉链，换洗方便", "亲肤柔软，越洗越软"]),

        new("STORAGE", "收纳用品", 1.5, "📦", 45, "storage box organizer", 19, 199,
            ["{brand} {room}{material}{feature}{noun}"],
            new()
            {
                ["brand"] = HomeBrands,
                ["room"] = ["", "衣柜", "厨房", "桌面", "浴室"],
                ["material"] = ["透明PP", "布艺", "竹木", "亚克力", "塑料", "金属"],
                ["feature"] = ["可叠加", "带盖", "抽屉式", "折叠", "带轮", "大容量", "防尘"],
                ["noun"] = ["收纳盒", "收纳箱", "收纳柜", "收纳筐", "收纳袋", "置物架", "收纳篮", "鞋盒"]
            },
            [new("规格", 15, "小号", "中号", "大号")],
            ["食品级材质，无异味", "可叠放设计，节省空间", "透明可视，找东西一眼看到", "加厚承重，不易变形"]),

        new("LAMP", "台灯", 1, "💡", 50, "desk lamp", 69, 899,
            ["{brand} {series}{#1-9}{suffix} {feature}{noun}"],
            new()
            {
                ["brand"] = HomeBrands,
                ["series"] = ["L", "Lumi ", "Read "],
                ["suffix"] = ["", " Pro", " Max"],
                ["feature"] = ["国AA级护眼", "全光谱", "无线充电", "智能感光", "学习", "夹子式", "氛围", "书桌"],
                ["noun"] = ["台灯", "护眼灯", "LED台灯"]
            },
            [new("颜色", 0, "白色", "黑色", "原木色")],
            ["无可视频闪，长时间阅读不累眼", "Ra97 高显色，还原真实色彩", "无极调光调色", "折叠灯臂，角度随心调"]),

        new("YOGA", "瑜伽垫", 1, "🧘", 140, "yoga mat", 49, 399,
            ["{brand} {material}{thickness}{feature}{noun} {gender}"],
            new()
            {
                ["brand"] = OutdoorBrands,
                ["material"] = ["TPE", "天然橡胶", "NBR", "PU", "硅胶", "软木"],
                ["thickness"] = ["4mm", "6mm", "8mm", "10mm", ""],
                ["feature"] = ["防滑", "加宽", "双面", "加长", "体位线", "初学者"],
                ["noun"] = ["瑜伽垫", "健身垫", "普拉提垫"],
                ["gender"] = ["", "男女通用", "女士"]
            },
            [new("颜色", 0, "雾紫", "豆绿", "藏青", "樱粉")],
            ["双面防滑纹理，出汗也不打滑", "高回弹，保护膝盖", "附赠绑带，携带方便", "无异味环保材质"]),

        new("TENT", "户外露营", 1, "⛺", 110, "camping tent", 199, 2999,
            ["{brand} {scene}{people}{season}{feature}帐篷", "{brand} {scene}{size}{canopy}天幕", "{brand} {scene}{chair}折叠椅",
             "{brand} {scene}{temperature}{bag}睡袋", "{brand} {scene}{light}露营灯", "{brand} {scene}{table}露营桌", "{brand} {scene}{mat}防潮垫"],
            new()
            {
                ["brand"] = OutdoorBrands,
                ["scene"] = ["", "户外", "便携", "自驾", "野营", "公园"],
                ["people"] = ["单人", "双人", "2-3人", "3-4人", "5-8人"],
                ["season"] = ["", "四季", "三季"],
                ["feature"] = ["速开", "防暴雨", "轻量化", "自动", "双层", "防晒", "加厚"],
                ["size"] = ["", "3x3m ", "4x6m ", "5x8m "],
                ["canopy"] = ["防晒", "黑胶", "蝶形", "加厚银胶", "六角", "方形"],
                ["chair"] = ["克米特", "月亮", "铝合金", "躺椅式", "导演"],
                ["temperature"] = ["", "-5℃", "0℃", "10℃"],
                ["bag"] = ["信封式", "木乃伊式", "羽绒", "加厚", "夏季"],
                ["light"] = ["充电", "复古马灯", "LED", "太阳能"],
                ["table"] = ["蛋卷", "铝合金", "折叠", "IGT"],
                ["mat"] = ["充气", "蛋巢", "加厚", "铝膜", "自动充气"]
            },
            [new("颜色", 0, "军绿", "沙色", "橙色")],
            ["3000mm 防水指数，雨天无忧", "一体式速开，60 秒搭好", "双层通风设计，夏天不闷", "附送地钉与收纳袋"]),

        new("DIAPER", "母婴用品", 1.5, "🍼", 30, "baby diapers", 79, 299,
            ["{brand} {series}{season}{feature}{tag}{noun}"],
            new()
            {
                ["brand"] = ["小树", "贝壳宝贝", "暖暖", "柔芽", "安心宝", "小羊驼", "棉宝", "初生"],
                ["series"] = ["柔芯", "云感", "轻薄", "羽感", "金装", "有机", "安睡", ""],
                ["season"] = ["", "夏季", "冬季", "四季"],
                ["feature"] = ["超薄透气", "夜用", "瞬吸", "超柔", "弱酸", ""],
                ["tag"] = ["", "新生儿", "婴儿", "宝宝"],
                ["noun"] = ["纸尿裤", "拉拉裤", "成长裤", "纸尿片"]
            },
            [new("尺码", 0, "NB", "S", "M", "L", "XL"), new("规格", 40, "单包", "箱装")],
            ["3D 立体护围，防侧漏", "瞬吸干爽，一夜安睡", "弱酸亲肤表层，呵护娇嫩肌肤", "尿显提醒，换片更及时"]),

        new("PETFOOD", "宠物食品", 2, "🐾", 25, "pet food cat dog", 49, 499,
            ["{brand} {tag}{life}{feature}{noun}"],
            new()
            {
                ["brand"] = ["毛孩子", "喵汪", "暖爪", "鲜宠", "汪星", "喵小鲜", "宠爱", "毛球", "汪喵", "宠乐"],
                ["tag"] = ["", "全价", "进口原料", "高蛋白", "天然", "营养"],
                ["life"] = ["", "室内", "绝育", "老年"],
                ["feature"] = ["无谷", "冻干双拼", "鲜肉", "低敏", "鱼肉", "鸡肉", "增肥", "美毛"],
                ["noun"] = ["幼猫猫粮", "成猫猫粮", "全阶段猫粮", "幼犬狗粮", "小型犬成犬粮", "中大型犬成犬粮", "猫罐头", "狗零食", "冻干", "猫条"]
            },
            [new("规格", 60, "1.5kg", "4kg", "10kg")],
            ["粗蛋白 ≥40%，鲜肉为第一原料", "添加益生菌，呵护肠胃", "颗粒大小适口，挑食也爱吃", "独立分装，开袋保鲜"])
    ];
}
