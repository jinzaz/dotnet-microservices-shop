# Elasticsearch + IK 中文分词插件。
# IK 插件必须与 ES 版本号完全一致（9.1.5 的 ES 只能装 9.1.5 的 IK），可用版本见
# https://release.infinilabs.com/analysis-ik/stable/ 。升级 ES 时两边一起改 ES_VERSION。
#   docker build -f docker/elasticsearch/Dockerfile -t harbor.local/msa-shop/elasticsearch-ik:9.1.5 docker/elasticsearch
ARG ES_VERSION=9.1.5
FROM docker.elastic.co/elasticsearch/elasticsearch:${ES_VERSION}
ARG ES_VERSION
RUN bin/elasticsearch-plugin install --batch \
      "https://release.infinilabs.com/analysis-ik/stable/elasticsearch-analysis-ik-${ES_VERSION}.zip"
# 自定义词典（品牌名、新品类词等 IK 默认切不对的词）：挂载到 /usr/share/elasticsearch/config/analysis-ik/
# 并在其中的 IKAnalyzer.cfg.xml 里配置 ext_dict；改词典后需重建索引才能影响已写入的文档。
