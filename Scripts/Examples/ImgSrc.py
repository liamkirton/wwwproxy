# --------------------------------------------------------------------------------
# WwwProxy\Scripts\Examples\ImgSrc.py
#
# Copyright ©2008 Liam Kirton <liam@int3.ws>
# --------------------------------------------------------------------------------
# ImgSrc.py
#
# Created: 10/04/2008
# --------------------------------------------------------------------------------

# Parameter Types

# WwwProxy.ProxyRequest
# .Id
# .Pass
# .SkipRemainingHandlers
# .Header
# .Data

# WwwProxy.ProxyResponse
# .Id
# .Pass
# .SkipRemainingHandlers
# .Completable
# .Header
# .Contents
	
# --------------------------------------------------------------------------------

import clr
clr.AddReference('WwwProxy')
from WwwProxy import *

import re

# --------------------------------------------------------------------------------

class WwwProxyFilter(object):

	# ----------------------------------------------------------------------------
	
	def __init__(self):
		pass

	# ----------------------------------------------------------------------------
	
	def pre_request_filter(self, request):
		pass

	# ----------------------------------------------------------------------------

	def post_request_filter(self, request):
		pass
	
	# ----------------------------------------------------------------------------
	
	def pre_response_filter(self, request, response):
		# If we have a valid, completable response
		if response.Completable and response.Contents != None:
			# Substitute all <img src="..." /> for <img src="new_img_url" />
			new_img_url = 'http://crazymonk.org/images/hypnotoad.jpg'
			img_sub_re = re.compile('(img.*?src=[\'\"])(.*?)([\'\"].*?>)', re.DOTALL | re.I | re.M)
			response.Contents = img_sub_re.sub('\g<1>' + new_img_url + '\g<3>', response.Contents)
	
	# ----------------------------------------------------------------------------
	
	def post_response_filter(self, request, response):
		pass
	
	# ----------------------------------------------------------------------------

# --------------------------------------------------------------------------------

